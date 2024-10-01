using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace VoiceTranslate.Pages
{
    public class HomeModel : PageModel
    {
        private readonly string assemblyApiKey = "YOUR_ASSEMBLYAI_API_KEY";
        private readonly string translateApiKey = "YOUR_RAPIDAPI_TRANSLATE_API_KEY";
        private readonly string ttsApiKey = "YOUR_RAPIDAPI_TTS_API_KEY";

        [BindProperty]
        public IFormFile UploadedFile { get; set; }

        [BindProperty]
        public string Language { get; set; }

        [BindProperty]
        public string Voice { get; set; }

        public string Transcription { get; set; }
        public string Translation { get; set; }
        public string AudioUrl { get; set; }

        public string Label3 { get; set; } = "Voice Translation Application";
        public string Label4 { get; set; } = "Translation Results";
        public string Label2 { get; set; } = "Enjoy your translated audio and text.";

        public async Task<IActionResult> OnPostAsync()
        {
            if (UploadedFile != null)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", UploadedFile.FileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await UploadedFile.CopyToAsync(stream);
                }

                var audioUrl = await UploadAudio(filePath);
                if (!string.IsNullOrEmpty(audioUrl))
                {
                    Transcription = await GetTranscription(audioUrl);
                    if (!string.IsNullOrEmpty(Transcription))
                    {
                        Translation = await TranslateText(Transcription, Language);
                        AudioUrl = await ConvertTextToSpeech(Translation, Voice);

                        if (string.IsNullOrEmpty(AudioUrl))
                        {
                            ModelState.AddModelError(string.Empty, "Failed to convert text to speech.");
                        }
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Failed to get transcription.");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Failed to upload audio.");
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Please select an audio file to upload.");
            }

            return Page();
        }

        private async Task<string> UploadAudio(string filePath)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://api.assemblyai.com/v2/upload"),
                    Headers =
                    {
                        { "authorization", assemblyApiKey }
                    },
                    Content = new ByteArrayContent(await System.IO.File.ReadAllBytesAsync(filePath))
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/octet-stream")
                        }
                    }
                };

                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    return json["upload_url"].ToString();
                }
            }
        }

        private async Task<string> GetTranscription(string audioUrl)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://api.assemblyai.com/v2/transcript"),
                    Headers =
                    {
                        { "authorization", assemblyApiKey }
                    },
                    Content = new StringContent($"{{\"audio_url\":\"{audioUrl}\"}}")
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/json")
                        }
                    }
                };

                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    var transcriptId = json["id"].ToString();

                    return await PollTranscriptionResult(transcriptId);
                }
            }
        }

        private async Task<string> PollTranscriptionResult(string transcriptId)
        {
            using (var client = new HttpClient())
            {
                var url = $"https://api.assemblyai.com/v2/transcript/{transcriptId}";
                while (true)
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri(url),
                        Headers =
                        {
                            { "authorization", assemblyApiKey }
                        }
                    };

                    using (var response = await client.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadAsStringAsync();
                        var json = JObject.Parse(body);
                        var status = json["status"].ToString();

                        if (status == "completed")
                        {
                            return json["text"].ToString();
                        }
                        else if (status == "failed")
                        {
                            return "Transcription failed.";
                        }
                    }

                    await Task.Delay(5000);
                }
            }
        }

        private async Task<string> TranslateText(string text, string targetLanguage)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://deep-translate1.p.rapidapi.com/language/translate/v2"),
                    Headers =
                    {
                        { "x-rapidapi-key", translateApiKey },
                        { "x-rapidapi-host", "deep-translate1.p.rapidapi.com" }
                    },
                    Content = new StringContent($"{{\"q\":\"{text}\",\"source\":\"en\",\"target\":\"{targetLanguage}\"}}")
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/json")
                        }
                    }
                };

                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    return json["data"]["translations"]["translatedText"].ToString();
                }
            }
        }

        private async Task<string> ConvertTextToSpeech(string text, string voiceCode)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://text-to-speech27.p.rapidapi.com/speech"),
                    Headers =
                    {
                        { "x-rapidapi-key", ttsApiKey },
                        { "x-rapidapi-host", "text-to-speech27.p.rapidapi.com" }
                    },
                    Content = new StringContent($"{{\"text\":\"{text}\",\"lang\":\"{voiceCode}\"}}")
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/json")
                        }
                    }
                };

                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    return json["audio_url"].ToString();
                }
            }
        }
    }
}






