using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckCompatiblePresentation()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var work = Path.Combine(Environment.CurrentDirectory, ".cache", "compatible-presentation", Guid.NewGuid().ToString("N"));
        var keys = new TextProviderKeys(work);
        const string endpoint = "https://example.org/v1";
        var credentials = new CompatibleCredentials("synthetic-id", "synthetic-secret", "synthetic-key");
        keys.Save(endpoint, credentials);
        var clear = keys.Read(endpoint + "/");
        var bytes = File.ReadAllBytes(Directory.GetFiles(Path.Combine(work, "text-provider-keys")).Single());
        Check(clear.ClientId == credentials.ClientId && clear.ClientSecret == credentials.ClientSecret && clear.ApiKey == credentials.ApiKey &&
            !Encoding.UTF8.GetString(bytes).Contains("synthetic"), "text credentials round-trip through current-user Windows protection without plaintext files");
        Check(!keys.HasSaved("https://other.example.org/v1") && keys.Read("https://other.example.org/v1").ClientSecret == "" &&
            !keys.HasSaved(endpoint + "/other"), "saved text credentials are bound to the complete endpoint, including its base path");
        try { keys.Save(endpoint, new("incomplete", "")); Check(false, "partial credentials"); }
        catch (CompatibleException) { Check(keys.Read(endpoint).ClientSecret == credentials.ClientSecret, "invalid replacement credentials leave the previous encrypted value intact"); }
        keys.Save("https://other.example.org/v1", new("other-id", "other-secret")); keys.Remove(endpoint);
        Check(!keys.HasSaved(endpoint) && keys.HasSaved("https://other.example.org/v1"), "clearing text credentials affects only the chosen endpoint");
        var file = Directory.GetFiles(Path.Combine(work, "text-provider-keys")).Single(); File.WriteAllBytes(file, [1,2,3]);
        try { keys.Read("https://other.example.org/v1"); Check(false, "corrupt protected file"); }
        catch (CompatibleException e) { Check(e.Message == CompatibleText.StorageFailure && !e.Message.Contains(work), "corrupt protected credentials report a safe, actionable message"); }

        int requests = 0; bool rejected = false;
        using var client = new CompatibleTextClient(new CompatibleHandler(request =>
        {
            requests++;
            Check(request.Headers.Authorization == null && request.Headers.GetValues("CF-Access-Client-Id").Single() == "synthetic-id" &&
                request.Headers.GetValues("CF-Access-Client-Secret").Single() == "synthetic-secret", "Windows model discovery sends both user-entered Access values while allowing a blank API key");
            return new HttpResponseMessage(rejected ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            { Content = new StringContent(rejected ? "private body" : """{"data":[{"id":"synthetic-a"},{"id":"synthetic-b"}]}""", Encoding.UTF8, "application/json") };
        }));
        var settings = new AppSettings { Provider = "claude", Model = "haiku" };
        var fixture = new SettingsWindow(settings, [], null, client, Path.Combine(work, "ui"));
        var tabs = Contents(fixture).OfType<TabControl>().Single();
        var queryTab = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "查询与模型");
        var connections = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "账号连接");
        var selectors = Contents((DependencyObject)queryTab.Content).OfType<ComboBox>().ToArray();
        var primary = selectors[0]; var initial = (ModelChoice)primary.SelectedItem;
        PasswordBox Field(string name) => Contents(fixture).OfType<PasswordBox>().Single(x => AutomationProperties.GetName(x) == name);
        var id = Field("CF Access Client ID"); var secret = Field("CF Access Client Secret");
        var status = Contents(fixture).OfType<TextBlock>().Single(x => AutomationProperties.GetName(x) == "自定义文本服务连接状态");
        var load = Contents(fixture).OfType<Button>().Single(x => AutomationProperties.GetName(x) == "读取自定义文本服务模型列表");
        Check(requests == 0 && Field("API Key（可选）").Password.Length == 0 && !primary.Items.OfType<ModelChoice>().Any(x => x.Provider == "compatible"),
            "opening settings does not fetch text models or invent a default custom model");
        load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(requests == 0 && status.Text.Contains("Client ID"), "Windows explains missing Access credentials before issuing a request");
        id.Password = "synthetic-id"; secret.Password = "synthetic-secret";
        load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(requests == 1 && primary.Items.OfType<ModelChoice>().Count(x => x.Provider == "compatible") == 2 && ((ModelChoice)primary.SelectedItem) == initial,
            "authenticated discovery adds exactly the returned model IDs and keeps the user's current primary model");
        Check(selectors[1].Items.OfType<ModelChoice>().Any(x => x.Id == "synthetic-b") && selectors[2].Items.OfType<ModelChoice>().Any(x => x.Id == "synthetic-a"),
            "discovered text models are also available to the secondary model and sense judge");
        primary.SelectedItem = primary.Items.OfType<ModelChoice>().Single(x => x.Provider == "compatible" && x.Id == "synthetic-b");
        Check(((ModelChoice)primary.SelectedItem).Id == "synthetic-b" && settings.Provider == "claude" && !Directory.Exists(Path.Combine(work, "ui")),
            "model selection stays an unsaved draft and discovery never saves entered credentials");
        rejected = true; load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(requests == 2 && status.Text.Contains("鉴权失败") && !status.Text.Contains("private"), "Windows shows a clear sanitized authentication failure with no anonymous retry");
        var url = Contents(fixture).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "自定义文本服务 Base URL");
        url.Text = "https://other.example.org/v1";
        Check(id.Password == "" && secret.Password == "" && !primary.Items.OfType<ModelChoice>().Any(x => x.Provider == "compatible"),
            "editing the endpoint clears draft secrets and its discovered model list");
        load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(requests == 2 && status.Text.Contains("Client ID"), "changing endpoints cannot forward credentials from the previous connection");
        fixture.Close();
    }
    private sealed class CompatibleHandler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(run(request)); }
}
