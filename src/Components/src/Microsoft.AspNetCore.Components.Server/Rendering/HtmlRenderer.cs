
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components.Server.Rendering
{
    /// <summary>
    /// A <see cref="Renderer"/> that produces HTML.
    /// </summary>
    public class HtmlRenderer : Renderer
    {
        private static readonly HashSet<string> SelfClosingElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
        };
        private readonly HashSet<Type> ExcludedValueTypes = new HashSet<Type>
        {
            typeof(Delegate)
        };

        /// <summary>
        /// Initializes a new instance of <see cref="HtmlRenderer"/>.
        /// </summary>
        /// <param name="serviceProvider">The <see cref="IServiceProvider"/> to use to instantiate components.</param>
        public HtmlRenderer(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        /// <inheritdoc />
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Renders a component into a sequence of <see cref="string"/> fragments that represent the textual representation
        /// of the HTML produced by the component.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="IComponent"/>.</typeparam>
        /// <param name="initialParameters">A <see cref="ParameterCollection"/> with the initial parameters to render the component.</param>
        /// <returns>A sequence of <see cref="string"/> fragments that represent the HTML text of the component.</returns>
        public IEnumerable<string> RenderComponent<T>(ParameterCollection initialParameters) where T : IComponent
        {
            return RenderComponent(typeof(T), initialParameters);
        }

        /// <summary>
        /// Renders a component into a sequence of <see cref="string"/> fragments that represent the textual representation
        /// of the HTML produced by the component.
        /// </summary>
        /// <param name="componentType">The type of the <see cref="IComponent"/>.</param>
        /// <param name="initialParameters">A <see cref="ParameterCollection"/> with the initial parameters to render the component.</param>
        /// <returns>A sequence of <see cref="string"/> fragments that represent the HTML text of the component.</returns>
        private IEnumerable<string> RenderComponent(Type componentType, ParameterCollection initialParameters)
        {
            var tree = CreateInitialRender(componentType, initialParameters);

            var frames = tree.GetFrames();

            if (frames.Count == 0)
            {
                return Array.Empty<string>();
            }
            else
            {
                var (segments, newPosition) = RenderFrames(frames, 0, frames.Count);
                Debug.Assert(newPosition == frames.Count);
                return segments;
            }
        }

        private (IEnumerable<string>, int newPosition) RenderFrames(ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
        {
            IEnumerable<string> segments = Array.Empty<string>();
            IEnumerable<string> nextSegments;
            var nextPosition = position;
            var endPosition = position + maxElements;
            while (position < endPosition)
            {
                (nextSegments, nextPosition) = RenderCore(frames, position, maxElements);
                if (position == nextPosition)
                {
                    throw new InvalidOperationException("We didn't consume any input.");
                }
                position = nextPosition;
                segments = segments.Concat(nextSegments);
            }

            return (segments, nextPosition);
        }

        private (IEnumerable<string> segments, int nextPosition) RenderCore(
            ArrayRange<RenderTreeFrame> frames,
            int position,
            int length)
        {
            var frame = frames.Array[position];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                    return RenderElement(frames, position);
                case RenderTreeFrameType.Attribute:
                    return RenderAttributes(frames, position, 1);
                case RenderTreeFrameType.Text:
                    return (new[] { frame.TextContent }, ++position);
                case RenderTreeFrameType.Markup:
                    return (new[] { frame.MarkupContent }, ++position);
                case RenderTreeFrameType.Component:
                    return RenderChildComponent(frames, position);
                case RenderTreeFrameType.Region:
                    return RenderFrames(frames, position + 1, frame.RegionSubtreeLength - 1);
                case RenderTreeFrameType.ElementReferenceCapture:
                case RenderTreeFrameType.ComponentReferenceCapture:
                    return (Array.Empty<string>(), ++position);
                default:
                    throw new InvalidOperationException("Invalid element frame type.");
            }
        }

        private (IEnumerable<string> segments, int nextPosition) RenderChildComponent(ArrayRange<RenderTreeFrame> frames, int position)
        {
            var frame = frames.Array[position];
            var childComponentRenderTree = GetCurrentRenderTree(frame.ComponentId);
            var childFrames = childComponentRenderTree.GetFrames();
            var (segments, _) = RenderFrames(childFrames, 0, childFrames.Count);
            return (segments, position + frame.ComponentSubtreeLength);
        }

        private (IEnumerable<string> segments, int nextPosition) RenderElement(ArrayRange<RenderTreeFrame> frames, int position)
        {
            var frame = frames.Array[position];
            IEnumerable<string> elementSegments = new[] { "<", frame.ElementName };
            var (attributes, afterAttributes) = RenderAttributes(frames, position + 1, frame.ElementSubtreeLength - 1);
            elementSegments = elementSegments.Concat(attributes);
            var remainingElements = frame.ElementSubtreeLength + position - afterAttributes;
            if (remainingElements > 0)
            {
                elementSegments = elementSegments.Concat(new[] { ">" });
                var (children, afterElement) = RenderChildren(frames, afterAttributes, remainingElements);
                elementSegments = elementSegments.Concat(children).Concat(new[] { "</", frame.ElementName, ">" });
                Debug.Assert(afterElement == position + frame.ElementSubtreeLength);
                return (elementSegments, afterElement);
            }
            else
            {
                elementSegments = elementSegments.Concat(
                    SelfClosingElements.Contains(frame.ElementName) ?
                    new[] { " />" } :
                    new[] { ">", "</", frame.ElementName, ">" });
                Debug.Assert(afterAttributes == position + frame.ElementSubtreeLength);
                return (elementSegments, afterAttributes);
            }
        }

        private (IEnumerable<string> children, int afterElement) RenderChildren(ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
        {
            if (maxElements == 0)
            {
                return (Array.Empty<string>(), position);
            }

            return RenderCore(frames, position, maxElements);
        }

        private (IEnumerable<string> attributes, int newPosition) RenderAttributes(ArrayRange<RenderTreeFrame> frames, int position, int maxElements)
        {
            if (maxElements == 0)
            {
                return (Array.Empty<string>(), position);
            }

            var attributes = new List<string>();
            for (var i = 0; i < maxElements; i++)
            {
                var candidateIndex = position + i;
                var frame = frames.Array[candidateIndex];
                if (frame.FrameType != RenderTreeFrameType.Attribute)
                {
                    return (attributes, candidateIndex);
                }
                if (frame.AttributeValue != null && !ExcludedValueTypes.Any(et => et.IsAssignableFrom(frame.AttributeValue.GetType())))
                {
                    attributes.Add($" {frame.AttributeName}={frame.AttributeValue}");
                }
            }

            return (attributes, position + maxElements);
        }

        private RenderTreeBuilder CreateInitialRender(Type componentType, ParameterCollection initialParameters)
        {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);

            RenderRootComponent(componentId, initialParameters);

            var tree = GetCurrentRenderTree(componentId);
            return tree;
        }
    }
}

