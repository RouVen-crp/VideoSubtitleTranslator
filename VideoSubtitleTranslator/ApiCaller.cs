using System.Net;
using System.Text;
using System.Text.Json;

namespace VideoSubtitleTranslator;

public static class ApiCaller
{
    private static string ApiKey
    {
        get
        {
            var key = Environment.GetEnvironmentVariable(GlobalRuntimeConfig.Current.Llm.ApiKeyEnv);
            if (!string.IsNullOrEmpty(key)) return key;

            Console.Write($"Invalid Api Key, env={GlobalRuntimeConfig.Current.Llm.ApiKeyEnv}");
            return "";
        }
    }

    public static async Task<string> CallApi(string modelType, string systemRolePrompt, string prompt)
    {
        var maxRetries = Math.Max(0, GlobalRuntimeConfig.Current.Llm.RetryCount);
        const int baseDelayMs = 1000;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, GlobalRuntimeConfig.Current.Llm.TimeoutSeconds));
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

                var requestBody = new
                {
                    model = modelType,
                    messages = new[]
                    {
                        new { role = "system", content = systemRolePrompt },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = GlobalRuntimeConfig.Current.Llm.MaxTokens,
                    temperature = GlobalRuntimeConfig.Current.Llm.Temperature
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(GlobalRuntimeConfig.Current.Llm.BaseUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxRetries ||
                    (ex.StatusCode.HasValue && (int)ex.StatusCode.Value < 500 &&
                     ex.StatusCode.Value != HttpStatusCode.TooManyRequests))
                    throw;

                var delay = baseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delay);
                Console.WriteLine($"请求失败，第{attempt + 1}次重试，等待{delay}ms...");
            }
            catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
            {
                if (attempt == maxRetries) throw;

                var delay = baseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delay);
                Console.WriteLine($"请求超时，第{attempt + 1}次重试，等待{delay}ms...");
            }
            catch (Exception ex) when (attempt == maxRetries)
            {
                Console.WriteLine($"失败: {ex.Message}");
                throw;
            }

        throw new InvalidOperationException("重试逻辑异常");
    }
}