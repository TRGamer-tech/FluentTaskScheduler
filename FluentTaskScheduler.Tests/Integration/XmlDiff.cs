using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FluentTaskScheduler.Tests.Integration
{
    /// <summary>Compares two task definitions element by element, ignoring formatting.</summary>
    internal static class XmlDiff
    {
        /// <summary>Flattens a task XML document into "path = value" lines.</summary>
        public static List<string> Flatten(string xml, params string[] ignoredElements)
        {
            var lines = new List<string>();
            var doc = XDocument.Parse(xml);
            Walk(doc.Root!, "", lines, new HashSet<string>(ignoredElements, StringComparer.Ordinal));
            return lines;
        }

        private static void Walk(XElement element, string path, List<string> lines, HashSet<string> ignored)
        {
            if (ignored.Contains(element.Name.LocalName)) return;

            var siblings = element.Parent?.Elements(element.Name).ToList();
            string index = siblings is { Count: > 1 } ? $"[{siblings.IndexOf(element)}]" : "";
            string self = $"{path}/{element.Name.LocalName}{index}";

            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                lines.Add($"{self}/@{attribute.Name.LocalName} = {attribute.Value}");

            if (!element.HasElements)
                lines.Add($"{self} = {element.Value}");
            else
                foreach (var child in element.Elements()) Walk(child, self, lines, ignored);
        }

        /// <summary>Lines only in <paramref name="before"/> are prefixed "-", lines only in <paramref name="after"/> "+".</summary>
        public static List<string> Diff(string before, string after, params string[] ignoredElements)
        {
            var a = Flatten(before, ignoredElements);
            var b = Flatten(after, ignoredElements);
            var result = new List<string>();
            result.AddRange(a.Except(b).Select(l => "- " + l));
            result.AddRange(b.Except(a).Select(l => "+ " + l));
            return result;
        }
    }
}
