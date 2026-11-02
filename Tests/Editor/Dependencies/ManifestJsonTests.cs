using System;
using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="ManifestJson"/>: strict validation of Packages/manifest.json text and lookup of
    /// root field value offsets (used for text-level insertion without rewriting consumer data).
    /// </summary>
    public class ManifestJsonTests
    {
        [Test]
        public void Parse_EmptyObject_Succeeds()
        {
            Assert.DoesNotThrow(() => ManifestJson.Parse("{}"));
            Assert.DoesNotThrow(() => ManifestJson.Parse("  \r\n\t{ }  \n"));
        }

        [Test]
        public void Parse_RealisticManifest_Succeeds()
        {
            const string manifest = "{\n" +
                                    "  \"dependencies\": {\n" +
                                    "    \"com.unity.ugui\": \"1.0.0\",\n" +
                                    "    \"com.example.local\": \"file:../Local\"\n" +
                                    "  },\n" +
                                    "  \"scopedRegistries\": [\n" +
                                    "    { \"name\": \"A\", \"url\": \"https://example.com\", \"scopes\": [\"com.a\"] }\n" +
                                    "  ],\n" +
                                    "  \"testables\": [],\n" +
                                    "  \"flag\": true, \"other\": false, \"nothing\": null,\n" +
                                    "  \"numbers\": [0, -1, 2.5, 1e10, -3.25E-2]\n" +
                                    "}";

            Assert.DoesNotThrow(() => ManifestJson.Parse(manifest));
        }

        [Test]
        public void Parse_EscapesInStrings_Succeed()
        {
            const string manifest = "{\"a\":\"quote \\\" slash \\\\ \\/ \\b\\f\\n\\r\\t \\u0041\"}";

            Assert.DoesNotThrow(() => ManifestJson.Parse(manifest));
        }

        [Test]
        public void Parse_Null_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() => ManifestJson.Parse(null));

            Assert.That(ex.Message, Does.Contain("Манифест пуст"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\r\n\t")]
        public void Parse_EmptyOrWhitespace_ThrowsFormatException(string text)
        {
            var ex = Assert.Throws<FormatException>(() => ManifestJson.Parse(text));

            Assert.That(ex.Message, Does.Contain("Манифест пуст"));
        }

        [TestCase("[]")]
        [TestCase("[1,2]")]
        [TestCase("\"text\"")]
        [TestCase("42")]
        [TestCase("true")]
        public void Parse_RootIsNotObject_ThrowsFormatException(string text)
        {
            var ex = Assert.Throws<FormatException>(() => ManifestJson.Parse(text));

            Assert.That(ex.Message, Does.Contain("Корень манифеста должен быть JSON-объектом"));
        }

        [TestCase("{")]
        [TestCase("{\"a\"")]
        [TestCase("{\"a\":")]
        [TestCase("{\"a\":1")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{,}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{a:1}")]
        [TestCase("{'a':1}")]
        [TestCase("{\"a\":[1,]}")]
        [TestCase("{\"a\":[1 2]}")]
        [TestCase("{\"a\":tru}")]
        [TestCase("{\"a\":01}")]
        [TestCase("{\"a\":+1}")]
        [TestCase("{\"a\":\"unterminated}")]
        [TestCase("{\"a\":\"bad \\x escape\"}")]
        [TestCase("{\"a\":\"bad \\u12 escape\"}")]
        [TestCase("{\"a\":\"line\nbreak\"}")]
        public void Parse_MalformedJson_ThrowsFormatException(string text)
        {
            Assert.Throws<FormatException>(() => ManifestJson.Parse(text));
        }

        [Test]
        public void Parse_TrailingGarbage_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => ManifestJson.Parse("{\"a\":1} x"));
            Assert.Throws<FormatException>(() => ManifestJson.Parse("{\"a\":1}{\"b\":2}"));
        }

        [Test]
        public void Parse_DuplicateRootKey_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() => ManifestJson.Parse("{\"a\":1,\"a\":2}"));

            Assert.That(ex.Message, Does.Contain("Повторяющееся поле манифеста: a"));
        }

        [Test]
        public void Parse_DuplicateKeyInNestedObject_IsAllowed()
        {
            // Only root fields are tracked; the nested dependencies map is the consumer's data.
            Assert.DoesNotThrow(() => ManifestJson.Parse("{\"a\":{\"b\":1,\"b\":2}}"));
        }

        [Test]
        public void Parse_ExcessiveNesting_ThrowsFormatException()
        {
            string tooDeep = "{\"a\":" + new string('[', 200) + new string(']', 200) + "}";
            string shallow = "{\"a\":" + new string('[', 50) + new string(']', 50) + "}";

            Assert.Throws<FormatException>(() => ManifestJson.Parse(tooDeep));
            Assert.DoesNotThrow(() => ManifestJson.Parse(shallow));
        }

        [Test]
        public void FindRootValue_ReturnsOffsetOfValueStart()
        {
            const string manifest = "{\"a\": 1, \"b\":   [1], \"c\":{\"d\":2}, \"e\":\"text\"}";

            var json = ManifestJson.Parse(manifest);

            Assert.AreEqual('1', manifest[json.FindRootValue("a")]);
            Assert.AreEqual('[', manifest[json.FindRootValue("b")]);
            Assert.AreEqual('{', manifest[json.FindRootValue("c")]);
            Assert.AreEqual('"', manifest[json.FindRootValue("e")]);
        }

        [Test]
        public void FindRootValue_MissingOrNestedKey_ReturnsMinusOne()
        {
            var json = ManifestJson.Parse("{\"a\":{\"nested\":1}}");

            Assert.AreEqual(-1, json.FindRootValue("nested"));
            Assert.AreEqual(-1, json.FindRootValue("missing"));
            Assert.AreEqual(-1, json.FindRootValue(""));
        }

        [Test]
        public void FindRootValue_IsCaseSensitive()
        {
            var json = ManifestJson.Parse("{\"scopedRegistries\":[]}");

            Assert.GreaterOrEqual(json.FindRootValue("scopedRegistries"), 0);
            Assert.AreEqual(-1, json.FindRootValue("ScopedRegistries"));
        }

        [Test]
        public void FindRootValue_EmptyObject_ReturnsMinusOne()
        {
            var json = ManifestJson.Parse("{}");

            Assert.AreEqual(-1, json.FindRootValue("dependencies"));
        }
    }
}
