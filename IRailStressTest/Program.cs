using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;


namespace IRailStressTest
{
    class Program
    {
        private readonly static Random r = new Random();

        static void Main(string[] args)
        {
            var testSets =
                new List<string>();
            for (int i = 0; i <= 17; i++)
            {
                testSets.Add(i.ToString());
            }


            // We want a throughput of 200/s
            // This should run ~1minute
            int maxNumberOfTests = 100;
            // After 'timeout' seconds, we'll stop testing
            int timeOut = 60;
            int spread = 30;


            var index = "";
            if (args.Length > 0)
            {
                index = "-" + args[0];
            }

            var resultDestination = $"results-{DateTime.Now:yyyy-MM-dd HH:mm:ss}{index}.csv";

            EnableLogging();
            Log.Information("IRail Stresstest");
            Log.Information("I'm sorry, Bert. Ben made me do this.");
            ThreadPool.GetAvailableThreads(out int workerThreadsAv, out int totalThreads);
            ThreadPool.GetMaxThreads(out int workerThreadsMax, out int totalThreadsMax);
            Log.Information($"Threadpool has {workerThreadsAv}/{workerThreadsMax} worker threads availableF");
            Log.Information($"Threadpool has {totalThreads}/{totalThreadsMax} total threads availableF");

            Log.Information("Generating queries...");
            var queries = GenerateQueries(testSets, maxNumberOfTests);
            Log.Information("Unleashing the beast...");
            var results = RunQueries(queries, timeOut, spread);
            Log.Information("Writing results...");
            results.Sort();
            File.WriteAllLines(resultDestination, new List<string> {CsvHeader});
            File.AppendAllLines(resultDestination, results);

            Log.Information($"Done. You can get your results by analyzing {resultDestination}");
        }


        private static List<string> GenerateQueries
            (IEnumerable<string> testSets, int maxNumberOfTests)
        {
            var queryList = new List<string>();

            var minuteDiff = 0;

            while (queryList.Count < maxNumberOfTests)
            {
                foreach (var testSet in testSets)
                {
                    var testSetpath = $"queries/round{testSet}.jsonstream";
                    var cases = File.ReadAllLines(testSetpath);
                    foreach (var @case in cases)
                    {
                        if (queryList.Count >= maxNumberOfTests)
                        {
                            break;
                        }

                        var jsonCase = JObject.Parse(@case);
                        var result = CreateQueryFor(jsonCase, minuteDiff);
                        queryList.Add(result);
                    }
                }

                minuteDiff++;
            }

            return queryList;
        }


        public static string CreateQueryFor(JObject test, int timeDrift = 0)
        {
            var dep = test.GetValue("departureStop").ToString();
            var arr = test.GetValue("arrivalStop").ToString();
            var depTime = DateTime.Parse(test.GetValue("departureTime").ToString());


            depTime.AddMinutes(timeDrift);

            return $"https://api.irail.be/connections/?to={arr}&" +
                   $"from={dep}&" +
                   $"date={depTime:ddMMyy}&time={depTime:HHmm}&" +
                   $"timeSel=depart";
        }

        private static List<string> RunQueries(List<string> queries, int timeOut, int spread)
        {
            var deadline = DateTime.Now.AddSeconds(timeOut);

            var results = new ConcurrentBag<string>();
            /*
            foreach (var query in queries)
            {
                results.Add(RunTestCase(query, deadline, spread));
            }/*/
            Parallel.ForEach(queries, 
                new ParallelOptions(){MaxDegreeOfParallelism = 192},
                (query) =>
            {
                results.Add(RunTestCase(query, deadline, spread));
                
            });
            
            // */

            var resultStrings = new List<string>(results);

            return resultStrings;
        }

        private static Lazy<HttpClient> LocalHttpClient = new Lazy<HttpClient>(() =>
        {
            HttpClientHandler hch = new HttpClientHandler();
            hch.Proxy = null;
            hch.UseProxy = false;
            hch.UseCookies = false;
            hch.AllowAutoRedirect = false;
            hch.PreAuthenticate = false;
            hch.CheckCertificateRevocationList = false;
            var client = new HttpClient(hch);

            client.DefaultRequestHeaders.Add("user-agent",
                "IRailStressTest-Anyways/0.0.1 (anyways.eu; pieter@anyways.eu)");
            client.DefaultRequestHeaders.Add("accept", "application/json");
            client.Timeout = TimeSpan.FromMilliseconds(15000);

            return client;
        });

        /// <summary>
        /// Runs the given testcase. Returns 
        /// </summary>
        /// <param name="test">The JSON-object containing the test data</param>
        /// <returns>A comma-seperated string, containing {query start time},{time needed},{response size|FAILED},{query}</returns>
        public static string RunTestCase(string queryString, DateTime deadline, int spread)
        {
            var client = LocalHttpClient.Value;

//            var wait = r.Next(0, spread);
//            Thread.Sleep(wait * 1000);


            var start = DateTime.Now;
            if (start > deadline)
            {
                return $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},0,TIMEOUT - NOT STARTED,{queryString}";
            }

            HttpResponseMessage response = null;
            try
            {
                response = client.GetAsync(new Uri(queryString))
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Log.Information(e.ToString());

                var errMsg = e.Message;
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    errMsg += ": " + e.Message;
                }

                return $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},0,ERROR {errMsg},{queryString}";
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                var endFailed = DateTime.Now;

                var timeNeededFailed = (int) (endFailed - start).TotalMilliseconds;

                return
                    $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},{timeNeededFailed},FAILED HTTP:{response?.StatusCode},{queryString}";
            }

            var data = response.Content.ReadAsStringAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();


            var end = DateTime.Now;

            var timeNeeded = (int) (end - start).TotalMilliseconds;

            //           Log.Information($"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},{timeNeeded},{data.Length},{queryString}");
            return $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},{timeNeeded},{data.Length},{queryString}";
        }


        public static string CsvHeader =
            "QueryDate,QueryHour,NeededTimeMilliSec,ResponseLength,Query";

        private static void EnableLogging()
        {
            // initialize serilog.
            var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logFile = Path.Combine("logs", $"log-{date}.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File(new JsonFormatter(), logFile)
                .WriteTo.Console()
                .CreateLogger();
        }
    }
}