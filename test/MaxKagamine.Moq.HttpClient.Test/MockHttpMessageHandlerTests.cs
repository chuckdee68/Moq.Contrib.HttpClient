﻿using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace MaxKagamine.Moq.HttpClient.Test
{
    public class MockHttpMessageHandlerTests
    {
        [Fact]
        public async Task MatchesAnyRequest()
        {
            // Just as one would create a Mock<HttpMessageHandler>, but using a subclass instead
            // which provides the request helpers and direct mocking of the protected SendAsync()
            var handler = new MockHttpMessageHandler();
            var client = handler.CreateClient(); // Equivalent to `new HttpClient(handler.Object, false)`

            var response = new HttpResponseMessage()
            {
                Content = new StringContent("foo")
            };

            handler.SetupAnyRequest()
                .ReturnsAsync(response);

            // All requests made with HttpClient go through the handler's SendAsync() which we've mocked
            (await client.GetAsync("http://localhost")).Should().BeSameAs(response);
            (await client.PostAsync("https://example.com/foo", new StringContent("data"))).Should().BeSameAs(response);
            (await client.GetStringAsync("https://example.com/bar")).Should().Be("foo");

            // Verify methods are provided matching the setup helpers
            handler.VerifyAnyRequest(Times.Exactly(3));
        }

        [Fact]
        public async Task MatchesRequestByUrl()
        {
            var handler = new MockHttpMessageHandler(MockBehavior.Strict);
            var client = handler.CreateClient();

            // Here we're basing the language off the url, but since it's Moq, we could also specify
            // a setup that checks the Accept-Language header or any other criteria (a helper is
            // provided to simplify checking the HttpRequestMessage; see other test below)
            var enUrl = "https://example.com/en-US/hello";
            var jaUrl = "https://example.com/ja-JP/hello";

            // The helpers return the same fluent api as the regular Setup, so we can use the normal
            // Moq methods for more complex responses
            handler.SetupRequest(enUrl)
                .Returns(async (HttpRequestMessage request, CancellationToken cancellationToken) =>
                    new HttpResponseMessage()
                    {
                        Content = new StringContent($"Hello, {await request.Content.ReadAsStringAsync()}")
                    });

            handler.SetupRequest(jaUrl)
                .Returns(async (HttpRequestMessage request, CancellationToken cancellationToken) =>
                    new HttpResponseMessage()
                    {
                        Content = new StringContent($"こんにちは、{await request.Content.ReadAsStringAsync()}")
                    });

            // Imagine we have a service that returns a greeting for a given locale
            async Task<string> GetGreeting(string locale, string name)
            {
                var response = await client.PostAsync($"https://example.com/{locale}/hello", new StringContent(name));
                return await response.Content.ReadAsStringAsync();
            }

            // Call the "service" which we expect to make the requests set up above
            string enGreeting = await GetGreeting("en-US", "world");
            string jaGreeting = await GetGreeting("ja-JP", "世界");

            enGreeting.Should().Be("Hello, world");
            jaGreeting.Should().Be("こんにちは、世界"); // Konnichiwa, sekai

            // This handler was created with MockBehavior.Strict which throws for invocations without setups
            Func<Task> esAttempt = () => GetGreeting("es-ES", "mundo");
            await esAttempt.Should().ThrowAsync<MockException>("a setup for Spanish was not configured");

            handler.VerifyRequest(enUrl, Times.Once());
            handler.VerifyRequest(jaUrl, Times.Once());
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public async Task MatchesRequestByMethod(string methodStr)
        {
            var handler = new MockHttpMessageHandler(MockBehavior.Strict);
            var client = handler.CreateClient();

            var method = new HttpMethod(methodStr); // Normally you'd use HttpMethod.Get, etc.
            var url = "https://example.com";
            var expected = $"This is {methodStr}!";

            handler.SetupRequest(method, url)
                .ReturnsAsync(new HttpResponseMessage()
                {
                    Content = new StringContent(expected)
                });

            var response = await client.SendAsync(new HttpRequestMessage(method, url));
            var actual = await response.Content.ReadAsStringAsync();

            actual.Should().Be(expected);

            // Ensure this isn't simply matching any request
            Func<Task> otherMethodAttempt = () => client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url));
            await otherMethodAttempt.Should().ThrowAsync<MockException>("the setup should not match a PATCH request");
        }

        [Fact]
        public async Task MatchesCustomPredicate()
        {
            var handler = new MockHttpMessageHandler();
            var client = handler.CreateClient();

            // Simulating posting a song to a music API
            var url = new Uri("https://example.com/api/songs");
            var token = "auth token obtained somehow";
            var model = new
            {
                Artist = "regulus feat. 初音ミク Append (Dark)",
                Title = "Schwarzer Regen",
                Album = "そこにいたこと",
                Url = "https://vocadb.net/S/70731"
            };

            // Set up a response for a request with this track
            handler
                .SetupRequest(HttpMethod.Post, url, async request =>
                {
                    // Here we can parse the request json
                    var json = JObject.Parse(await request.Content.ReadAsStringAsync());
                    return json.Value<string>("title") == model.Title;
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Created));

            // A request without a valid auth token should fail (the last setup takes precedence)
            handler.SetupRequest(r => r.Headers.Authorization?.Parameter != token)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            // Imaginary service method
            async Task CreateTrack(object trackModel, string authToken)
            {
                var json = JsonConvert.SerializeObject(trackModel, new JsonSerializerSettings()
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }

            // Create the track
            await CreateTrack(model, token);

            // Attempt to create the track again, this time without a valid token
            // (Rolling two unit tests into one if we were really testing a service)
            Func<Task> unauthorizedAttempt = () => CreateTrack(model, "expired token");
            await unauthorizedAttempt.Should().ThrowAsync<HttpRequestException>("this should 400");
        }

        [Fact]
        public void VerifyHelpersThrowAsExpected()
        {
            // TODO: Check times and failMessage as well

            throw new NotImplementedException();
        }

        [Fact]
        public void CanMockProtectedSendAsync()
        {
            // TODO: One might wish to check that a method passes its CancellationToken to the http call

            throw new NotImplementedException();
        }
    }
}
