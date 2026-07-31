using System;
using System.Drawing;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JnnBoost
{
    public class LoginForm : Form
    {
        private Label labelTitulo = null!;
        private Label labelSubtitulo = null!;
        private Label labelSenha = null!;
        private TextBox txtSenha = null!;
        private Button btnEntrar = null!;
        private Label labelStatus = null!;
        private PictureBox logoPicture = null!;

        // Agora aponta para a API própria via HTTPS (Postgres + ASP.NET Core
        // atrás de um proxy Caddy).
        // Ex.: "https://192.168.15.13/api/validar" (rede local/teste)
        //      "https://seu-dominio.com/api/validar" (produção, com Let's Encrypt)
        private const string UrlServidor =
            "https://SEU_IP_OU_DOMINIO/api/validar";

        private static readonly Color CorFundo = Color.FromArgb(26, 26, 46);
        private static readonly Color CorBotao = Color.FromArgb(22, 33, 62);
        private static readonly Color CorTexto = Color.FromArgb(0, 212, 255);
        private static readonly Color CorSucesso = Color.FromArgb(0, 204, 102);
        private static readonly Color CorErro = Color.FromArgb(255, 68, 68);
        private static readonly Color CorMuted = Color.FromArgb(106, 122, 138);
        private static readonly Color CorBorda = Color.FromArgb(0, 180, 220);

        public bool Autenticado { get; private set; } = false;

        public LoginForm()
        {
            InitUI();
        }

        private void InitUI()
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = CorFundo;
            this.Size = new Size(380, 430);
            this.Text = "JnnBoost — Autenticação";
            this.Font = new Font("Consolas", 9f);

            logoPicture = new PictureBox
            {
                Size = new Size(120, 120),
                Location = new Point(130, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            try
            {
                var stream = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetManifestResourceStream("JnnBoost.icone.ico");
                if (stream != null)
                    logoPicture.Image = new Icon(stream).ToBitmap();
            }
            catch { }

            labelTitulo = new Label
            {
                Text = "JnnBoost",
                Font = new Font("Consolas", 20f, FontStyle.Bold),
                ForeColor = CorTexto,
                AutoSize = false,
                Size = new Size(360, 35),
                Location = new Point(10, 150),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            labelSubtitulo = new Label
            {
                Text = "GAME OPTIMIZER",
                Font = new Font("Consolas", 9f),
                ForeColor = CorMuted,
                AutoSize = false,
                Size = new Size(360, 20),
                Location = new Point(10, 185),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            labelSenha = new Label
            {
                Text = "Chave de acesso:",
                Font = new Font("Consolas", 9f),
                ForeColor = CorMuted,
                AutoSize = true,
                Location = new Point(40, 230),
                BackColor = Color.Transparent
            };

            txtSenha = new TextBox
            {
                Location = new Point(40, 252),
                Size = new Size(290, 26),
                PasswordChar = '●',
                BackColor = Color.FromArgb(13, 13, 26),
                ForeColor = CorTexto,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 11f)
            };
            txtSenha.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) _ = TentarLoginAsync();
            };

            btnEntrar = new Button
            {
                Text = "ENTRAR",
                Location = new Point(40, 295),
                Size = new Size(290, 34),
                BackColor = CorBotao,
                ForeColor = CorTexto,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btnEntrar.FlatAppearance.BorderColor = CorBorda;
            btnEntrar.FlatAppearance.BorderSize = 1;
            btnEntrar.Click += async (s, e) => await TentarLoginAsync();

            labelStatus = new Label
            {
                Text = "",
                Font = new Font("Consolas", 8.5f),
                ForeColor = CorMuted,
                AutoSize = false,
                Size = new Size(360, 60),
                Location = new Point(10, 345),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

            Controls.AddRange(new Control[] {
                logoPicture, labelTitulo, labelSubtitulo,
                labelSenha, txtSenha, btnEntrar, labelStatus
            });
        }

        // -----------------------------------------------
        // LOGIN VIA POST COM CORPO JSON
        // (antes era GET com parâmetros na URL - migrado para não
        //  expor a chave de licença em logs/histórico de proxy)
        // -----------------------------------------------
        private async Task TentarLoginAsync()
        {
            string senha = txtSenha.Text.Trim();

            if (string.IsNullOrEmpty(senha))
            {
                MostrarStatus("Digite a chave de acesso.", CorErro);
                return;
            }

            btnEntrar.Enabled = false;
            MostrarStatus("Verificando chave...", CorMuted);

            string hwid = ObterHardwareId();

            try
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true
                };

                // ATENÇÃO - TEMPORÁRIO: enquanto a API usa certificado
                // auto-assinado (rede local, sem domínio público ainda),
                // o .NET rejeitaria a conexão por padrão (certificado não
                // confiável). Essa linha aceita o certificado mesmo assim.
                //
                // ISSO PRECISA SER REMOVIDO quando migrar para um domínio
                // real com certificado Let's Encrypt (aí a validação
                // padrão do .NET volta a funcionar normalmente e protege
                // de verdade contra ataques man-in-the-middle).
                handler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true;

                using var cliente = new HttpClient(handler);
                cliente.Timeout = TimeSpan.FromSeconds(15);

                var payload = new { senha = senha, hwid = hwid };
                var conteudo = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await cliente.PostAsync(UrlServidor, conteudo);
                var body = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(body);
                string status = doc.RootElement.GetProperty("status").GetString() ?? "";

                switch (status)
                {
                    case "ativado":
                        MostrarStatus("Chave ativada neste computador!", CorSucesso);
                        await Task.Delay(800);
                        Autenticado = true;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                        break;

                    case "autorizado":
                        MostrarStatus("Acesso autorizado!", CorSucesso);
                        await Task.Delay(600);
                        Autenticado = true;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                        break;

                    case "bloqueado":
                        MostrarStatus(
                            "Esta chave já está vinculada\na outro computador.",
                            CorErro);
                        txtSenha.Clear();
                        break;

                    case "expirada":
                        MostrarStatus(
                            "Esta chave de acesso expirou.\nEntre em contato para renovar.",
                            CorErro);
                        txtSenha.Clear();
                        break;

                    case "invalida":
                        MostrarStatus("Chave inválida. Tente novamente.", CorErro);
                        txtSenha.Clear();
                        break;

                    default:
                        MostrarStatus($"Erro do servidor: {status}", CorErro);
                        break;
                }
            }
            catch (HttpRequestException)
            {
                MostrarStatus(
                    "Sem conexão com o servidor.\nVerifique sua internet.",
                    CorErro);
            }
            catch (TaskCanceledException)
            {
                MostrarStatus("Tempo esgotado. Tente novamente.", CorErro);
            }
            catch (JsonException)
            {
                MostrarStatus(
                    "Resposta inválida do servidor.\nVerifique a URL da implantação.",
                    CorErro);
            }
            catch (Exception ex)
            {
                MostrarStatus($"Erro: {ex.Message}", CorErro);
            }
            finally
            {
                btnEntrar.Enabled = true;
                txtSenha.Focus();
            }
        }

        // -----------------------------------------------
        // EXIBIR STATUS NA TELA
        // -----------------------------------------------
        private void MostrarStatus(string msg, Color cor)
        {
            if (labelStatus.InvokeRequired)
                labelStatus.Invoke(new Action<string, Color>(MostrarStatus), msg, cor);
            else
            {
                labelStatus.ForeColor = cor;
                labelStatus.Text = msg;
            }
        }

        // -----------------------------------------------
        // HARDWARE ID — hash SHA256 de CPU + placa mãe + disco
        // -----------------------------------------------
        private string ObterHardwareId()
        {
            var sb = new StringBuilder();

            try
            {
                var s = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in s.Get())
                    sb.Append(obj["ProcessorId"]?.ToString() ?? "");
            }
            catch { }

            try
            {
                var s = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in s.Get())
                    sb.Append(obj["SerialNumber"]?.ToString() ?? "");
            }
            catch { }

            try
            {
                var s = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
                foreach (ManagementObject obj in s.Get())
                { sb.Append(obj["SerialNumber"]?.ToString() ?? ""); break; }
            }
            catch { }

            string raw = sb.ToString();
            if (string.IsNullOrEmpty(raw)) raw = Environment.MachineName;

            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtSenha.Focus();
        }
    }
}