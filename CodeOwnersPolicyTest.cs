using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;

namespace SimCorp.IMS.Project.Policies.Tests
{
    [TestFixture]
    public class CodeOwnersPolicyTest
    {
        CodeOwnersPolicy codeOwnersPolicy = new CodeOwnersPolicy();
        IEnumerable<string> codeOwnersContent = new List<string>()
            {
                "# this is a comment.",
                "codegenerators\\ codegenerators@simcorp.com 1@simcorp.com",
                "codegenerators\\framework\\ codegeneratorsframework@simcorp.com 2@simcorp.com",
                "frontofficedata\\ frontofficedata@simcorp.com @fo tab@simcorp.com 3@simcorp.com",
                "frontofficedata\\test\\ frontofficedatatest@simcorp.com @fo @emptygro 4@simcorp.com",
                "# @fo       = @focph @fokiev",
                "# @focph    = mkal@simcorp.com ogg@simcorp.com",
                "# @fokiev   =             kiev@simcorp.com"
            };

        [Test]
        public void GetUserAliasTest()
        {
            // Arrange
            var expected = "ando";
            // Act
            var actual = codeOwnersPolicy.GetUserAlias("SCDOM\\ANDO");
            // Assert
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void GetCodeOwnersPathTest()
        {
            // Arrange
            var localItem = @"C:\workspaces\foart\IMS\FrontOfficeData\Test\AccountingAwareHoldingKeyTest.cs";
            var expected = @"C:\workspaces\foart\IMS\CODEOWNERS";
            // Act
            var actual = codeOwnersPolicy.GetCodeOwnersPath(localItem);
            // Assert
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void GetSubTreesTest()
        {
            // Arrange
            var expected = new List<string>()
            {
                "codegenerators",
                "codegenerators\\framework",
                "frontofficedata",
                "frontofficedata\\test"
            };
            // Act
            var actual = codeOwnersPolicy.GetSubTrees(codeOwnersContent);
            // Assert
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void GetGroupDefinitionsTest()
        {
            // Arrange
            var expected = new Dictionary<string, string>()
            {
                {"@fo", "@focph @fokiev" },
                {"@focph", "mkal@simcorp.com ogg@simcorp.com" },
                {"@fokiev", "kiev@simcorp.com" }
            };
            // Act
            var actual = codeOwnersPolicy.GetGroupDefinitions(codeOwnersContent);
            // Assert
            CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList());
            }

        [Test]
        public void GetRecursiveGroupDefinitionsTest()
        {
            // Arrange
            var expected = new Dictionary<string, string>()
            {
                {"@fo", "mkal@simcorp.com ogg@simcorp.com kiev@simcorp.com" },
                {"@focph", "mkal@simcorp.com ogg@simcorp.com" },
                {"@fokiev", "kiev@simcorp.com" }
            };
            // Act
            var groupDefinitions = codeOwnersPolicy.GetGroupDefinitions(codeOwnersContent);
            var actual = codeOwnersPolicy.GetRecursiveGroupDefinitions(groupDefinitions);
            // Assert
            CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList());
        }

        [Test]
        public void IsACodeOwnerTest()
        {
            // Arrange
            var line = "@focph mkal@simcorp.com ogg@simcorp.com";
            var userAlias = "ogg";
            // Act
            var actual = codeOwnersPolicy.IsACodeOwner(line, userAlias);
            // Assert
            Assert.AreEqual(true, actual);
        }

        [Test]
        public void IsNotACodeOwnerTest()
        {
            // Arrange
            var line = "@focph mkal@simcorp.com ogg@simcorp.com";
            var userAlias = "focph";
            // Act
            var actual = codeOwnersPolicy.IsACodeOwner(line, userAlias);
            // Assert
            Assert.AreEqual(false, actual);
        }

        [Test]
        public void GetCodeOwnersMailsTest()
        {
            // Arrange
            var expected = new Dictionary<string, List <string>>()
            {
                  { "codegenerators",            new List<string> { "codegenerators@simcorp.com",                                "1@simcorp.com" } },
                  { "codegenerators\\framework", new List<string> { "codegeneratorsframework@simcorp.com",                       "2@simcorp.com" } },
                  { "frontofficedata",           new List<string> { "frontofficedata@simcorp.com",     "@fo", "tab@simcorp.com", "3@simcorp.com" } },
                  { "frontofficedata\\test",     new List<string> { "frontofficedatatest@simcorp.com", "@fo", "@emptygro",       "4@simcorp.com" } }
            };
            // Act
            var actual = codeOwnersPolicy.GetCodeOwnersMails(codeOwnersContent);
            // Assert
            CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList());
        }
    }
}