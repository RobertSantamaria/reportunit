using ReportUnit.Logging;
using ReportUnit.Model;
using ReportUnit.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ReportUnit.Parser
{
    internal class XUnitV2 : IParser
    {
        private string _resultsFile;

        private Logger _logger = Logger.GetLogger();

        public Report Parse(string resultsFile)
        {
            _resultsFile = resultsFile;

            var doc = XDocument.Load(resultsFile);

            if (doc.Root == null)
            {
                throw new NullReferenceException();
            }

            var report = new Report
            {
                FileName = Path.GetFileNameWithoutExtension(resultsFile),
                AssemblyName = doc.Root.Attribute("name") != null ? doc.Root.Attribute("name").Value : null,
                TestRunner = TestRunner.XUnitV2
            };

            // run-info & environment values -> RunInfo
            var runInfo = CreateRunInfo(doc, report);
            if (runInfo != null)
            {
                report.AddRunInfo(runInfo.Info);
            }

            // report status messages
            var suites = doc.Descendants("collection");

            suites.AsParallel().ToList().ForEach(ts =>
            {
                var testSuite = new TestSuite();
                testSuite.Name = ts.Attribute("name").Value;

                // Suite Time Info
                testSuite.Duration = double.Parse(ts.Attribute("time").Value);

                // Test Cases
                ts.Descendants("test").AsParallel().ToList().ForEach(tc =>
                {
                    var test = new Test();

                    test.Name = tc.Attribute("name").Value;
                    test.Status = StatusExtensions.ToStatus(tc.Attribute("result").Value);

                    // main a master list of all status
                    // used to build the status filter in the view
                    report.StatusList.Add(test.Status);

                    // TestCase Time Info
                    test.Duration = double.Parse(tc.Attribute("time").Value);

                    // get test case level categories
                    var categories = this.GetCategories(tc, true);

                    // error and other status messages
                    test.StatusMessage = tc.Element("failure") != null
                            ? tc.Element("failure").Element("message").Value.Trim()
                            : "";
                    var stackTrace = tc.Element("failure") != null
                            ? tc.Element("failure").Element("stack-trace").Value.Trim()
                            : "";

                    test.StatusMessage += stackTrace;

                    testSuite.TestList.Add(test);
                });

                testSuite.Status = ReportUtil.GetFixtureStatus(testSuite.TestList);

                report.TestSuiteList.Add(testSuite);
            });

            //Sort category list so it's in alphabetical order
            report.CategoryList.Sort();

            return report;
        }

        /// <summary>
        /// Returns categories for the direct children or all descendents of an XElement
        /// </summary>
        /// <param name="elem">XElement to parse</param>
        /// <param name="allDescendents">If true, return all descendent categories.  If false, only direct children</param>
        /// <returns></returns>
        private HashSet<string> GetCategories(XElement elem, bool allDescendents)
        {
            //Define which function to use
            var parser = allDescendents
                ? new Func<XElement, string, IEnumerable<XElement>>((e, s) => e.Descendants(s))
                : new Func<XElement, string, IEnumerable<XElement>>((e, s) => e.Elements(s));

            //Grab unique categories
            var categories = new HashSet<string>();
            var hasCategories = parser(elem, "traits").Any();
            if (hasCategories)
            {
                var cats = parser(elem, "traits").Elements("trait").ToList();

                cats.ForEach(x =>
                {
                    var cat = x.Attribute("name").Value;
                    var val = x.Attribute("value").Value;
                    categories.Add(string.Concat(cat, ";" , val));
                });
            }

            return categories;
        }

        private RunInfo CreateRunInfo(XDocument doc, Report report)
        {
            if (doc.Element("assemblies") == null)
                return null;

            var runInfo = new RunInfo();
            runInfo.TestRunner = report.TestRunner;

            var env = doc.Descendants("assembly").First();
            runInfo.Info.Add("Test results file", _resultsFile);
            runInfo.Info.Add("Test framework", env.Attribute("test-framework").Value);
            runInfo.Info.Add("Assembly name", env.Attribute("name").Value);
            runInfo.Info.Add("Run date", env.Attribute("run-date").Value);
            runInfo.Info.Add("Run time", env.Attribute("run-time").Value);

            // report counts
            report.Total = double.Parse(env.Attribute("total").Value);
            report.Passed = double.Parse(env.Attribute("passed").Value);
            report.Failed = double.Parse(env.Attribute("failed").Value);
            report.Errors = double.Parse(env.Attribute("errors").Value);
            report.Skipped = double.Parse(env.Attribute("skipped").Value);

            // report duration
            report.Duration = double.Parse(env.Attribute("time").Value);

            return runInfo;
        }

        public XUnitV2() { }
    }
}
