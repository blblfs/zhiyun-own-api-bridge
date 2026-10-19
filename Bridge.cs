using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using HarmonyLib;

namespace OwnApi
{
    public sealed class BridgeDomainManager : AppDomainManager
    {
        public override void InitializeNewDomain(AppDomainSetup info)
        {
            // Keep the manager scoped to this application, not its CefSharp child processes.
            Environment.SetEnvironmentVariable("APPDOMAIN_MANAGER_ASM", null);
            Environment.SetEnvironmentVariable("APPDOMAIN_MANAGER_TYPE", null);
            Bridge.Log("manager initialized");
            Application.Idle += Bridge.Install;
        }
    }

    public sealed class Settings
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string TargetLanguage { get; set; }
    }

    public static class Api
    {
        public static string Endpoint(string baseUrl)
        {
            Uri uri;
            if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Base URL 必须为 HTTP/HTTPS 地址，不能带查询参数、密码或片段。");
            var url = uri.AbsoluteUri.TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
            return url + (uri.AbsolutePath == "/" ? "/v1/chat/completions" : "/chat/completions");
        }

        public static string Translate(Settings settings, string text)
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Model))
                throw new ArgumentException("请先填写自己的 API Key 和模型名。");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var payload = new {
                model = settings.Model.Trim(),
                messages = new[] {
                    new { role = "system", content = "Translate the user's text into " +
                        (string.IsNullOrWhiteSpace(settings.TargetLanguage) ? "简体中文" : settings.TargetLanguage) +
                        ". Preserve meaning, paragraph structure, and technical terms. Return only the translation." },
                    new { role = "user", content = text }
                },
                stream = false
            };
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(Endpoint(settings.BaseUrl));
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + settings.ApiKey.Trim();
            request.AllowAutoRedirect = false;
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            var body = Encoding.UTF8.GetBytes(serializer.Serialize(payload));
            request.ContentLength = body.Length;
            using (var output = request.GetRequestStream()) output.Write(body, 0, body.Length);
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    if ((int)response.StatusCode >= 300) throw new InvalidOperationException("接口返回重定向，已停止请求。");
                    var result = serializer.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                    var choices = result != null && result.ContainsKey("choices") ? result["choices"] as object[] : null;
                    if (choices == null || choices.Length == 0) throw new InvalidOperationException("接口没有返回 choices。");
                    var choice = choices[0] as Dictionary<string, object>;
                    var message = choice != null && choice.ContainsKey("message") ? choice["message"] as Dictionary<string, object> : null;
                    var content = message != null && message.ContainsKey("content") ? message["content"] as string : null;
                    if (content == null) throw new InvalidOperationException("接口没有返回 choices[0].message.content 文本。");
                    return content;
                }
            }
            catch (WebException ex)
            {
                using (var response = ex.Response as HttpWebResponse)
                {
                    throw new InvalidOperationException(response == null ? "接口连接失败：" + ex.Status :
                        "接口请求失败：HTTP " + (int)response.StatusCode + "。请检查地址、Key 和模型名。");
                }
            }
        }
    }

    public static class Bridge
    {
        static readonly string Folder = Path.GetFullPath(Environment.GetEnvironmentVariable("OWNAPI_HOME") ??
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "OwnApi"));
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static int installing;
        static int sequence;
        static Form mainForm;
        static MethodInfo render;
        public static void Log(string message)
        {
            try { File.AppendAllText(Path.Combine(Folder, "bridge.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); }
            catch { }
        }

        public static Settings Load()
        {
            return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"), Encoding.UTF8));
        }

        public static void Install(object sender, EventArgs args)
        {
            Form found = null;
            foreach (Form form in Application.OpenForms)
                if (form.GetType().FullName == "pdf.Form1") { found = form; break; }
            if (found == null || Interlocked.Exchange(ref installing, 1) != 0) return;
            Application.Idle -= Install;
            try
            {
                mainForm = found;
                var type = found.GetType();
                render = type.GetMethod("Paragraph_display", Flags, null, new[] { typeof(string), typeof(string) }, null);
                if (render == null) throw new MissingMethodException("Paragraph_display");
                var harmony = new Harmony("local.ownapi.translation");
                var translate = type.GetMethod("翻译", Flags, null, new[] { typeof(object) }, null);
                var input = type.GetMethod("UserControl输入翻译1_MyCustomTranslationEven", Flags);
                var selected = type.GetMethod("aspx_OnViewLButtonUp", Flags);
                if (translate == null || input == null || selected == null) throw new MissingMethodException("翻译入口与当前版本不符");
                harmony.Patch(translate, prefix: new HarmonyMethod(typeof(Bridge), "TranslationPrefix"));
                harmony.Patch(input, prefix: new HarmonyMethod(typeof(Bridge), "TranslationPrefix"));
                harmony.Patch(selected, prefix: new HarmonyMethod(typeof(Bridge), "SelectionPrefix"));
                mainForm.Text += " — 自有 API";
                AddMenu();
                Log("installed; login flag=" + LoginValue());
                Render("", "自有 API 模式已启用。通过顶部「自有 API」配置 Base URL、API Key、模型名后，可划选 PDF 或输入文字翻译。");
            }
            catch (Exception ex)
            {
                new Harmony("local.ownapi.translation").UnpatchAll("local.ownapi.translation");
                Log("install failed: " + ex);
                MessageBox.Show("自有 API 适配层加载失败，请查看本项目 .local/bridge.log。", "自有 API");
            }
        }

        static string LoginValue()
        {
            var type = mainForm.GetType().Assembly.GetType("pdf.ClassMain");
            return Convert.ToString(type.GetField("isLogin", BindingFlags.Public | BindingFlags.Static).GetValue(null));
        }

        public static bool TranslationPrefix(object __instance, object __0)
        {
            Queue(__instance as Form, Convert.ToString(__0));
            return false;
        }

        public static bool SelectionPrefix(object __instance)
        {
            try
            {
                var getter = __instance.GetType().Assembly.GetType("70A9F4B4").GetMethod("021C4600", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Queue(__instance as Form, Convert.ToString(getter.Invoke(null, null)));
            }
            catch (Exception) { Render("", "读取 PDF 划选文字失败。可通过「自有 API → 输入文字翻译」使用。"); }
            return false;
        }

        static void Queue(Form form, string text)
        {
            if (form == null || string.IsNullOrWhiteSpace(text)) return;
            var id = Interlocked.Increment(ref sequence);
            Render(text, "正在请求你的翻译接口…");
            Task.Run(delegate {
                try
                {
                    var result = Api.Translate(Load(), text);
                    if (id == Volatile.Read(ref sequence)) Render(text, result);
                    Log("translation completed; login flag=" + LoginValue());
                }
                catch (Exception ex)
                {
                    if (id == Volatile.Read(ref sequence)) Render(text, "自有 API 请求失败：" + ex.Message);
                    Log("translation failed: " + ex.GetType().Name);
                }
            });
        }

        static void Render(string source, string translation)
        {
            if (mainForm == null || mainForm.IsDisposed) return;
            Action action = delegate {
                try { render.Invoke(mainForm, new object[] { WebUtility.HtmlEncode(source), WebUtility.HtmlEncode(translation) }); }
                catch (Exception ex) { Log("render failed: " + ex.GetType().Name); }
            };
            if (mainForm.InvokeRequired) mainForm.BeginInvoke(action); else action();
        }

        static void AddMenu()
        {
            var menu = mainForm.GetType().GetField("dSkinToolStrip1", Flags).GetValue(mainForm) as ToolStrip;
            if (menu == null) throw new InvalidOperationException("找不到主菜单");
            var root = new ToolStripMenuItem("自有 API") { Alignment = ToolStripItemAlignment.Left, Overflow = ToolStripItemOverflow.Never };
            root.DropDownItems.Add("接口设置…", null, delegate { ShowSettings(); });
            root.DropDownItems.Add("输入文字翻译…", null, delegate {
                using (var dialog = new Form { Text = "自有 API · 输入文字", Size = new Size(620, 400), StartPosition = FormStartPosition.CenterParent })
                {
                    var text = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
                    var button = new Button { Text = "翻译", Dock = DockStyle.Bottom, Height = 38, DialogResult = DialogResult.OK };
                    dialog.Controls.Add(text); dialog.Controls.Add(button);
                    if (dialog.ShowDialog(mainForm) == DialogResult.OK) Queue(mainForm, text.Text);
                }
            });
            menu.Items.Insert(0, root);
        }

        static void ShowSettings()
        {
            try
            {
                var settings = Load();
                using (var dialog = new Form { Text = "自有 API · 接口设置", ClientSize = new Size(600, 240), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
                {
                    var labels = new[] { "Base URL", "API Key", "模型名", "目标语言" };
                    var values = new[] { settings.BaseUrl, settings.ApiKey, settings.Model, settings.TargetLanguage };
                    var inputs = new TextBox[4];
                    for (var i = 0; i < 4; i++)
                    {
                        dialog.Controls.Add(new Label { Text = labels[i], Left = 15, Top = 20 + i * 40, Width = 90 });
                        inputs[i] = new TextBox { Text = values[i], Left = 110, Top = 16 + i * 40, Width = 470, UseSystemPasswordChar = i == 1 };
                        dialog.Controls.Add(inputs[i]);
                    }
                    dialog.Controls.Add(new Label { Text = "Key 仅保存在本项目 .local/settings.json，请妥善保管该文件。", Left = 15, Top = 177, Width = 570 });
                    var save = new Button { Text = "保存", Left = 490, Top = 205, Width = 90 };
                    save.Click += delegate {
                        try
                        {
                            Api.Endpoint(inputs[0].Text);
                            settings.BaseUrl = inputs[0].Text.Trim(); settings.ApiKey = inputs[1].Text.Trim();
                            settings.Model = inputs[2].Text.Trim(); settings.TargetLanguage = inputs[3].Text.Trim();
                            File.WriteAllText(Path.Combine(Folder, "settings.json"), new JavaScriptSerializer().Serialize(settings), Encoding.UTF8);
                            dialog.Close();
                        }
                        catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "配置错误"); }
                    };
                    dialog.Controls.Add(save); dialog.ShowDialog(mainForm);
                }
            }
            catch (Exception ex) { MessageBox.Show(mainForm, ex.Message, "配置读取失败"); }
        }
    }
}
