using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DotNetEnv;
using Newtonsoft.Json;

namespace FMAI;

public class Jev {
    public record UrgencyQuestion(string type, string instructions);
    public record JevQuestion(UrgencyQuestion urgency);
    public record JevRequest(string state, string model, JevQuestion questions);

    private readonly string jevApiKey;

    private static readonly HttpClient Client = new HttpClient();
    public static readonly string JevEndpoint = "https://api.typesafe.ai/v1/systemone";

    public Jev() {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var toolDir = Path.GetDirectoryName(assemblyLocation);

        if (toolDir is null) {
            jevApiKey = "";
            Console.WriteLine("Failed to find directory the application is running from");
            Environment.Exit(1);
            return;
        }
        
        var envPath = Path.Combine(toolDir, ".env");
        Env.Load(envPath);
        jevApiKey = Environment.GetEnvironmentVariable("JEV_KEY") ?? "";
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jevApiKey);

        if (jevApiKey == "") {
            Console.WriteLine("Jev API key is missing from .env file");
            Environment.Exit(1);
        }
    }

    public Task<string> MakeJevRequest() {
        var jevPayload =
            new JevRequest(
                "Hi, I've been trying to connect my Stripe account for 3 days and the integration keeps failing. I'm losing sales. Please help ASAP.",
                "jev-latest",
                new JevQuestion(new UrgencyQuestion("noul", "Does this message express urgency?")));
        var jsonPayload = JsonConvert.SerializeObject(jevPayload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try {
            return Client.PostAsync(JevEndpoint, content)
                .ContinueWith(postTask => {
                    if (postTask.IsFaulted) {
                        return Task.FromResult("");
                    }
        
                    var response = postTask.Result;
        
                    return response.Content.ReadAsStringAsync();
                })
                .Unwrap()
                .ContinueWith(readTask => {
                    if (readTask.Result != "") {
                        string json = readTask.Result;
                        return Task.FromResult(json);
                    }

                    return Task.FromResult("");
                }, TaskScheduler.FromCurrentSynchronizationContext())
                .Unwrap();
        }
        catch (HttpRequestException e) {
            return Task.FromResult("");
        }
    }
}