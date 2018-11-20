// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Testing.xunit;
using Templates.Test.Infrastructure;
using Xunit;
using Xunit.Abstractions;

[assembly: AssemblyFixture(typeof(SeleniumServerFixture))]
// Turn off parallel test run for Edge as the driver does not support multiple Selenium tests at the same time
#if EDGE
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
#endif
[assembly: TestFramework("Templates.Test.Helpers.XunitExtensions.XunitTestFrameworkWithAssemblyFixture", "Templates.Test.Common")]
namespace Templates.Test
{
    public class MvcTemplateTest : BrowserTestBase
    {
        public MvcTemplateTest(BrowserFixture browserFixture, ITestOutputHelper output) : base(browserFixture, output)
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("F#", Skip = "https://github.com/aspnet/Templating/issues/673")]
        private void MvcTemplate_NoAuthImpl(string languageOverride)
        {
            RunDotNetNew("mvc", language: languageOverride);

            AssertDirectoryExists("Areas", false);
            AssertDirectoryExists("Extensions", false);
            AssertFileExists("urlRewrite.config", false);
            AssertFileExists("Controllers/AccountController.cs", false);

            var projectExtension = languageOverride == "F#" ? "fsproj" : "csproj";
            var projectFileContents = ReadFile($"{ProjectName}.{projectExtension}");
            Assert.DoesNotContain(".db", projectFileContents);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Tools", projectFileContents);
            Assert.DoesNotContain("Microsoft.VisualStudio.Web.CodeGeneration.Design", projectFileContents);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Tools.DotNet", projectFileContents);
            Assert.DoesNotContain("Microsoft.Extensions.SecretManager.Tools", projectFileContents);

            foreach (var publish in new[] { false, true })
            {
                using (var aspNetProcess = StartAspNetProcess(publish))
                {
                    TestBasicNavigation(aspNetProcess, NoAuthUrls);
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MvcTemplate_IndividualAuthImpl(bool useLocalDB)
        {
            RunDotNetNew("mvc", auth: "Individual", useLocalDB: useLocalDB);

            AssertDirectoryExists("Extensions", shouldExist: false);
            AssertFileExists("urlRewrite.config", shouldExist: false);
            AssertFileExists("Controllers/AccountController.cs", shouldExist: false);

            var projectFileContents = ReadFile($"{ProjectName}.csproj");
            if (!useLocalDB)
            {
                Assert.Contains(".db", projectFileContents);
            }

            RunDotNetEfCreateMigration("mvc");

            AssertEmptyMigration("mvc");

            foreach (var publish in new[] { false, true })
            {
                using (var aspNetProcess = StartAspNetProcess(publish))
                {
                    TestBasicNavigation(aspNetProcess, AuthUrls);
                }
            }
        }
    }
}
