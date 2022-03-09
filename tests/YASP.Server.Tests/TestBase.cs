using NUnit.Framework;

using System;
using System.IO;
using System.Threading.Tasks;

namespace YASP.Server.Tests
{
    public abstract class TestBase
    {
        public const string TESTDATA_DIRECTORY = "testdata";

        protected static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(120);

        [SetUp]
        public Task SetupAsync()
        {
            if (Directory.Exists(TESTDATA_DIRECTORY)) Directory.Delete(TESTDATA_DIRECTORY, recursive: true);

            Directory.CreateDirectory(TESTDATA_DIRECTORY);

            return Task.CompletedTask;
        }

        [TearDown]
        public Task TeardownAsync()
        {
            Directory.Delete(TESTDATA_DIRECTORY, recursive: true);

            return Task.CompletedTask;
        }
    }
}
