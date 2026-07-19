using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace NameCheapDNSUpdate
{
    class Program
    {


        //globals
        public static int intCheckTimerSEC;  //environmental variable, interval of how long to wait to check again
        public static int intCheckTimerMS;  //same as above just for Milliseconds for the timer

        //the last IP we successfully pushed to the API. null means "unknown", which forces an update.
        public static string? strLastPublishedIP = null;


        //API details
        public static string strDomain = "";  //domain to process
        public static List<string> strHosts = new List<string>();  //list of hosts to process
        public static string strDynamicDNSPassword = "";  //api password

        //one client for the life of the process, a client per request exhausts sockets
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };


        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Program Begin/FirstRun: " + Timestamp());

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(GlobalExceptionHandler);


            //startup tests
            Console.WriteLine("Startup Tests Begin: " + Timestamp());

            if (!startupCheck())
            {
                //non-zero so a container restart policy sees this as a failure, not a clean exit
                return 1;
            }

            //set a domain
            Domain domainToRun = new Domain();

            domainToRun.fullName = strDomain;
            domainToRun.Hosts = strHosts;


            //seed the last known IP from DNS so a restart doesn't force a needless update.
            //after this we track what we published ourselves, comparing against live DNS every
            //cycle would re-trigger updates for the length of the record's TTL.
            strLastPublishedIP = ResolveHostName(domainToRun.Hosts.First(), domainToRun);


            //planned endless loop for automation
            while (true)
            {

                //get the current public IP address
                Console.WriteLine("Program Run: " + Timestamp());
                Console.WriteLine("Checking Public IP...");

                var currentIP = await GetPublicIPAsync();


                //was there an error geting the public IP?
                if (currentIP == null)
                {
                    Console.WriteLine("Error getting public IP, doing nothing.");
                }
                else if (currentIP != strLastPublishedIP)
                {
                    if (strLastPublishedIP == null)
                    {
                        Console.WriteLine("No known DNS record, updating DNS");
                    }
                    else
                    {
                        Console.WriteLine("IPs do not match, updating DNS");
                    }

                    bool updated = await SetHostsIPAsync(domainToRun, currentIP, strDynamicDNSPassword);

                    //leave the state unknown on failure so the next cycle retries
                    strLastPublishedIP = updated ? currentIP : null;

                    Console.WriteLine(updated
                        ? "DNS Update Complete"
                        : "DNS Update failed, will retry in " + intCheckTimerSEC + " seconds.");
                }
                else
                {
                    Console.WriteLine("IPs match, nothing to do");
                }


                //sleep for the configured interval and repeat process
                Console.WriteLine("Sleeping for " + intCheckTimerSEC + " seconds.");
                await Task.Delay(intCheckTimerMS);


            }

        }

        static string Timestamp()
        {
            return DateTime.UtcNow.ToString("u");
        }

        static void GlobalExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;

            // Log the exception details
            Console.WriteLine($"Unhandled exception caught: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");

            // Perform any cleanup or resource release, if needed

            // Optionally, terminate the application
            if (e.IsTerminating)
            {
                Console.WriteLine("Application is terminating due to an unhandled exception.");
            }
        }


        public static bool startupCheck()
        {

            //check startup config.
            bool returnVal = true;


            //inializations
            //intCheckTimerSEC
            if (!TryInitializeCheckTimer())
            {
                returnVal = false;
            }

            //strDomain
            strDomain = (Environment.GetEnvironmentVariable("domain") ?? "").Trim();

            //strHosts
            strHosts = (Environment.GetEnvironmentVariable("hosts") ?? "")
                .Split(';')                                     //split hosts on semicolon
                .Select(host => host.Trim())
                .Where(host => host.Length > 0)
                .ToList();

            //strDynamicDNSPassword
            strDynamicDNSPassword = (Environment.GetEnvironmentVariable("dynamicDNSPassword") ?? "").Trim();  //api password


            //no blanks
            if (strDomain == "")
            {
                Console.WriteLine("domain environment variable cannnot be blank.");
                returnVal = false;
            }
            else if (strDomain.LastIndexOf('.') <= 0 || strDomain.EndsWith("."))
            {
                //guards the API call, and a domain with no dot used to throw out of Main
                Console.WriteLine("domain environment variable must be a fully qualified domain, e.g. example.com");
                returnVal = false;
            }

            if (strHosts.Count == 0)
            {
                Console.WriteLine("hosts environment variable cannnot be blank.");
                returnVal = false;
            }
            else
            {
                //hosts go into the query unescaped so '@' and '*' survive, so reject anything
                //that could break out of the parameter
                foreach (var host in strHosts.Where(host => !IsValidHost(host)))
                {
                    Console.WriteLine("hosts entry '" + host + "' contains unsupported characters.");
                    returnVal = false;
                }
            }

            if (strDynamicDNSPassword == "")
            {
                Console.WriteLine("dynamicDNSPassword environment variable cannnot be blank.");
                returnVal = false;
            }


            if (returnVal == false)
            {
                Console.WriteLine("System Startup Checks failed, please checked the errors and restart.");
            }


            return returnVal;

        }

        static bool IsValidHost(string host)
        {
            //'@' means the root of the domain and '*' is a wildcard record, both are legal
            //unencoded in a query string
            return host.Length > 0
                && host.All(c => char.IsAsciiLetterOrDigit(c)
                    || c == '-' || c == '_' || c == '.' || c == '@' || c == '*');
        }

        private static bool TryInitializeCheckTimer()
        {
            var timerValue = Environment.GetEnvironmentVariable("intCheckTimerSEC");

            if (string.IsNullOrWhiteSpace(timerValue))
            {
                Console.WriteLine("intCheckTimerSEC environment variable cannnot be blank.");
                return false;
            }

            if (!int.TryParse(timerValue.Trim(), out var parsedSeconds))
            {
                Console.WriteLine("Error Processing intCheckTimerSEC ENV.");
                return false;
            }

            //upper bound keeps the milliseconds conversion below int.MaxValue
            if (parsedSeconds < 1 || parsedSeconds > 86400)
            {
                Console.WriteLine("intCheckTimerSEC must be between 1 and 86400 seconds.");
                return false;
            }

            intCheckTimerSEC = parsedSeconds;
            intCheckTimerMS = intCheckTimerSEC * 1000;

            return true;
        }

        public static async Task<string?> GetPublicIPAsync()
        {

            //get current public IP

            string URL = @"https://api.ipify.org/?format=json";

            Console.WriteLine("Checking Public IP From: " + URL);


            try
            {
                //get current public IP
                string content = await SendRequestGetAsync(URL);

                PublicIP? response = JsonConvert.DeserializeObject<PublicIP>(content);

                if (!IsIPv4(response?.ip))
                {
                    Console.WriteLine("Public IP get failure: no usable IPv4 address in response.");
                    return null;
                }

                Console.WriteLine("Public IP: " + response!.ip);

                return response.ip;
            }
            catch (Exception e)
            {
                Console.WriteLine("Public IP get failure: " + e.Message);

                return null;
            }


        }

        static bool IsIPv4(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && IPAddress.TryParse(value, out var parsed)
                && parsed.AddressFamily == AddressFamily.InterNetwork;
        }

        //'@' is the API's name for the root of the domain, it is not a DNS label,
        //so '@.example.com' would never resolve
        public static string FullHostName(string host, Domain domain)
        {
            return host == "@" ? domain.fullName : host + "." + domain.fullName;
        }

        public static string? ResolveHostName(string host, Domain domain)
        {
            IPHostEntry hostEntry;

            //full hostname
            var hostName = FullHostName(host, domain);

            Console.WriteLine("Attempting to resolve " + hostName);

            try
            {
                //check DNS resolution
                hostEntry = Dns.GetHostEntry(hostName);


            }
            catch (Exception)
            {
                Console.WriteLine("resolution of " + hostName + " failed.");

                return null;
            }


            //a host can carry several records, and an AAAA record would never match our IPv4
            //public address, so take the first A record only
            var address = hostEntry?.AddressList
                ?.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

            if (address == null)
            {
                Console.WriteLine("resolution of " + hostName + " returned no IPv4 address.");

                return null;
            }

            Console.WriteLine(hostName + " resolves to: " + address.ToString());

            return address.ToString();
        }


        public static async Task<bool> SetHostsIPAsync(Domain domainToRun, string newIPAddress, string appPassword)
        {

            bool allSucceeded = true;

            //loop through each host and set the Update URL
            foreach (var host in domainToRun.Hosts)
            {
                Console.WriteLine("Updating host: " + FullHostName(host, domainToRun));

                //update the IP for all hosts on the domain
                string URL = BuildUpdateUrl(host, domainToRun.fullName, newIPAddress, appPassword);

                //never log the real password, container logs are not a secret store
                Console.WriteLine("Update DNS URL: " + BuildUpdateUrl(host, domainToRun.fullName, newIPAddress, "REDACTED"));

                try
                {

                    //set host lists
                    string content = await SendRequestGetAsync(URL);

                    Interfaceresponse? deserializedContentData;

                    //API response to object
                    XmlSerializer serializer = new XmlSerializer(typeof(Interfaceresponse));
                    using (StringReader reader = new StringReader(content))
                    {
                        deserializedContentData = (Interfaceresponse?)serializer.Deserialize(reader);
                    }

                    if (deserializedContentData == null)
                    {
                        Console.WriteLine("Update failed: could not read API response.");
                        allSucceeded = false;
                    }
                    else if (deserializedContentData.ErrCount > 0)
                    {
                        Console.WriteLine("Update Errors:" + deserializedContentData.Errors);
                        Console.WriteLine("Full Return Content: " + Scrub(content, appPassword));
                        allSucceeded = false;
                    }
                    else
                    {
                        Console.WriteLine("Update Success");
                    }

                }
                catch (Exception e)
                {
                    //an HTTP exception can quote the request it failed on, so scrub it too
                    Console.WriteLine("Error in Update Request: " + Scrub(e.Message, appPassword));
                    allSucceeded = false;
                }

            }

            Console.WriteLine("Host List Complete");

            return allSucceeded;

        }

        //last line of defence before anything derived from a request reaches the log.
        //the password should never appear in a response or an error, but assuming that
        //is how it ended up in the log in the first place.
        static string Scrub(string text, string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                return text;
            }

            return text
                .Replace(secret, "REDACTED")
                .Replace(Uri.EscapeDataString(secret), "REDACTED");
        }

        static string BuildUpdateUrl(string host, string domainName, string ip, string password)
        {
            //values are escaped, an unencoded '&' or '#' in a password silently corrupts the query.
            //host is the exception: '@' (root) and '*' (wildcard) are meaningful to the API and are
            //legal unencoded in a query, so escaping them to %40/%2A would change what we ask for.
            //startupCheck restricts hosts to characters that cannot break the query.
            return @"https://dynamicdns.park-your-domain.com/update?"
                + "host=" + host
                + "&domain=" + Uri.EscapeDataString(domainName)
                + "&password=" + Uri.EscapeDataString(password)
                + "&ip=" + Uri.EscapeDataString(ip);
        }

        public static async Task<string> SendRequestGetAsync(string url)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(url);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }


    }


    public class PublicIP
    {
        public string? ip { get; set; }
        public string? country { get; set; }
        public string? cc { get; set; }
    }


}
