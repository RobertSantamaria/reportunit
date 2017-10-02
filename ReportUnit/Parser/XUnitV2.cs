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

            XDocument doc = XDocument.Load(resultsFile);

            if (doc.Root == null)
            {
                throw new NullReferenceException();
            }

            XAttribute assembly = doc.Root.Attribute("name");

            Report report = new Report
            {
                FileName = Path.GetFileNameWithoutExtension(resultsFile),
                AssemblyName = assembly != null ? assembly.Value : "",
                TestRunner = TestRunner.XUnitV2
            };

            // run-info & environment values -> RunInfo
            RunInfo runInfo = CreateRunInfo(doc, report);
            if (runInfo != null)
            {
                report.AddRunInfo(runInfo.Info);
            }

            // report status messages
            IEnumerable<XElement> suites = doc.Descendants("collection");

            suites.AsParallel().ToList().ForEach(ts =>
            {
                TestSuite testSuite = new TestSuite();
                string suiteName = ts.Attribute("name").Value;
                testSuite.Name = suiteName != null ? suiteName : "";

                // Suite Time Info
                string suiteTime = ts.Attribute("time").Value;
                testSuite.Duration = suiteTime != null ? double.Parse(suiteTime) : 0;

                // Test Cases
                ts.Descendants("test").AsParallel().ToList().ForEach(tc =>
                {
                    Test test = new Test();

                    string testName = tc.Attribute("name").Value;
                    test.Name = testName != null ? testName : "";

                    string result = tc.Attribute("result").Value;
                    test.Status = result != null ? StatusExtensions.ToStatus(result) : Status.Unknown;

                    // main a master list of all status
                    // used to build the status filter in the view
                    report.StatusList.Add(test.Status);

                    // TestCase Time Info
                    string time = tc.Attribute("time").Value;
                    test.Duration = time != null ? double.Parse(time) : 0;

                    // get test case level categories
                    HashSet<string> categories = GetCategories(tc, true);
                    if (categories.Count > 0)
                    {
                        test.CategoryList = categories.ToList<string>();
                    }

                    // error and other status messages
                    XElement failure = tc.Element("failure");
                    if (failure != null)
                    {
                        //string exceptionType = failure.Attribute("exception-type").Value;
                        //test.StatusMessage = exceptionType != null ? string.Concat(exceptionType, Environment.NewLine) : "";

                        string message = failure.Element("message").Value;
                        test.StatusMessage = message != null ? message : "";

                        string stackTrace = failure.Element("stack-trace").Value;
                        test.StackTrace = stackTrace != null ? stackTrace : "";
                    }

                    //reason for skipping a test
                    XElement reason = tc.Element("reason");
                    if (reason != null)
                    {
                        test.StatusMessage = reason.Value;
                    }

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
            HashSet<string> categories = new HashSet<string>();
            bool hasCategories = parser(elem, "traits").Any();
            if (hasCategories)
            {
                List<XElement> cats = parser(elem, "traits").Elements("trait").ToList();

                cats.ForEach(x =>
                {
                    string cat = x.Attribute("name").Value;
                    string val = x.Attribute("value").Value;
                    categories.Add(string.Concat(cat, ":" , val));
                });
            }

            return categories;
        }

        private RunInfo CreateRunInfo(XDocument doc, Report report)
        {
            if (doc.Element("assemblies") == null)
                return null;

            RunInfo runInfo = new RunInfo();
            runInfo.TestRunner = report.TestRunner;

            XElement env = doc.Descendants("assembly").First();
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
