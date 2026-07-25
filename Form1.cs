using Microsoft.VisualBasic.Devices;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
// Vortice native wrappers are used for a stronger GPU stress path.
// We attempt to create a D3D11 device and run a render loop; if that
// fails (missing native support, permissions, or driver issues) we
// fallback to the GDI bitmap stress which works everywhere.
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct3D;
// do not import Vortice.Mathematics unqualified to avoid Color symbol collision
using System.Windows.Forms;

namespace JnnBoost
{
    public partial class Form1 : Form
    {
        private OverlayForm? overlay;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        // RAM monitor
        private CancellationTokenSource? ramMonitorCts = null;
        private bool ramMonitorActive = false;
        private readonly object ramMonitorLock = new object();
        // evita reentrada na rotina de análise de gargalo
        private int analisandoGargaloFlag = 0;
        private NotifyIcon trayIcon = new NotifyIcon();
        private ContextMenuStrip trayMenu = new ContextMenuStrip();
        // marca quando o botão Otimizar RAM foi pressionado (para diagnóstico)
        private DateTime? lastOptimizeRamPress = null;

        // Animação simples para o botão "Otimizar RAM" (toggle)
        private bool button3AnimTarget = false;
        private float button3AnimValue = 0f; // 0.0 = off, 1.0 = on
        private System.Windows.Forms.Timer button3AnimTimer = new System.Windows.Forms.Timer();

        // (No custom animation or inline notification — use default button appearance)

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }


        // default button appearance used (no custom paint)

        // Usa GlobalMemoryStatusEx para obter uso de memória mais preciso
        private float GetMemoryUsagePercent()
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                mem.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref mem))
                {
                    return mem.dwMemoryLoad; // já em percent
                }
            }
            catch { }
            // Fallback para ComputerInfo quando GlobalMemoryStatusEx não está disponível
            try
            {
                var info = new ComputerInfo();
                double total = info.TotalPhysicalMemory;
                double free = info.AvailablePhysicalMemory;
                return (float)(((total - free) / total) * 100.0);
            }
            catch { }
            return 0f;
        }

        private bool timerResolutionAtiva = false;

        // Paleta de cores centralizada
        private static readonly System.Drawing.Color CorTexto = System.Drawing.Color.FromArgb(0, 212, 255);
        private static readonly System.Drawing.Color CorSucesso = System.Drawing.Color.FromArgb(0, 204, 102);
        private static readonly System.Drawing.Color CorAviso = System.Drawing.Color.FromArgb(255, 170, 0);
        private static readonly System.Drawing.Color CorErro = System.Drawing.Color.FromArgb(255, 68, 68);
        private static readonly System.Drawing.Color CorCyan = System.Drawing.Color.FromArgb(0, 238, 255);

        // -----------------------------------------------
        // PROCESSOS NÃO-JOGO
        // -----------------------------------------------
        private static readonly string[] ProcessosNaoJogo = {
            "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",
            "iexplore", "chromium", "waterfox", "librewolf",
            "discord", "slack", "telegram", "whatsapp", "zoom",
            "skype", "teams", "msteams", "signal",
            "taskmgr", "perfmon", "resmon", "mmc", "regedit",
            "devenv", "code", "rider", "idea64", "pycharm",
            "obs64", "obs32", "streamlabs", "xsplit",
            "shadowplay", "medal", "outplayed",
            "explorer", "svchost", "System", "Registry", "smss",
            "csrss", "wininit", "winlogon", "services", "lsass",
            "dwm", "RuntimeBroker", "SearchHost",
            "StartMenuExperienceHost", "ShellExperienceHost",
            "TextInputHost", "MsMpEng", "avp", "avgnt",
            "avguard", "bdagent", "ekrn", "mbam", "ccSvcHst",
            "JnnBoost"
        };

        private static readonly string[] ProcessosParasitas = {
            "OneDrive", "Teams", "SkypeApp", "YourPhone",
            "Cortana", "SearchApp", "GameBarPresenceWriter",
            "WidgetService", "Widgets", "MicrosoftEdgeUpdate",
            "AdobeUpdateService", "CCleaner", "CCleaner64"
        };

        private static bool EhProcessoNaoJogo(string nome) =>
            ProcessosNaoJogo.Any(p =>
                nome.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);

        // -----------------------------------------------
        // CONSTRUTOR
        // -----------------------------------------------
        public Form1()
        {
            InitializeComponent();
            // default button behavior (no custom animation)
            cpuCounter.NextValue();
            timer.Interval = 1000;
            timer.Tick += UpdateMonitor;
            timer.Start();
            ConfigurarTooltips();
            ConfigurarTray();
            // configurar timer de animação do botão Otimizar RAM
            button3AnimTimer.Interval = 30; // animação suave
            button3AnimTimer.Tick += Button3AnimTimer_Tick;
            // no inline notification — using log and tray only
        }

        private void Button3AnimTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // aproxima o valor atual para o target
                float speed = 0.12f; // rapidez da transição
                if (button3AnimTarget)
                    button3AnimValue = Math.Min(1f, button3AnimValue + speed);
                else
                    button3AnimValue = Math.Max(0f, button3AnimValue - speed);

                // calcula cor interpolada entre off e on
                Color offBg = Color.FromArgb(22, 33, 62);
                Color onBg = Color.FromArgb(0, 204, 102);
                int r = (int)(offBg.R + (onBg.R - offBg.R) * button3AnimValue);
                int g = (int)(offBg.G + (onBg.G - offBg.G) * button3AnimValue);
                int b = (int)(offBg.B + (onBg.B - offBg.B) * button3AnimValue);
                Color bg = Color.FromArgb(r, g, b);

                if (button3 != null && !button3.IsDisposed)
                {
                    button3.BackColor = bg;
                }

                // parar timer quando chegar no target
                if ((button3AnimTarget && button3AnimValue >= 1f) || (!button3AnimTarget && button3AnimValue <= 0f))
                    button3AnimTimer.Stop();
            }
            catch { button3AnimTimer.Stop(); }
        }

        // -----------------------------------------------
        // TOOLTIPS
        // -----------------------------------------------
        private void ConfigurarTooltips()
        {
            var tip = new ToolTip()
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            tip.SetToolTip(button1, "Desativa Game DVR, ativa Game Mode,\nativa timer de 1ms e bloqueia notificações.");
            tip.SetToolTip(button2, "Ativa HAGS e prioridade de GPU.\nRequer reinício para efeito completo.");
            tip.SetToolTip(button3, "Encerra parasitas, desabilita Widgets\npermanentemente e libera RAM.");
            tip.SetToolTip(button4, "Apaga arquivos temporários e\nlibera espaço em disco.");
            tip.SetToolTip(button5, "Limpa DNS e otimiza TCP.\nNão reseta Winsock para evitar quebrar VPNs.");
            tip.SetToolTip(button6, "Testa CPU, RAM, disco, GPU e rede.\nIdentifica e corrige problemas automaticamente.");
            tip.SetToolTip(button7, "Detecta o jogo pela GPU e otimiza.\nDiscord e browsers são preservados.");
            tip.SetToolTip(button8, "Analisa compatibilidade CPU x GPU,\nmede uso real e corrige gargalos.");
            tip.SetToolTip(button9, "Desfaz todas as otimizações e\nrestora o Windows ao estado padrão.");
            tip.SetToolTip(button10, "Abre/fecha o overlay em linha\ncom CPU, GPU e RAM em tempo real.");
        }

        // -----------------------------------------------
        // TRAY
        // -----------------------------------------------
        private void ConfigurarTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Abrir JnnBoost", null, (s, e) => MostrarJanela());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Fechar", null, (s, e) => FecharApp());

            trayIcon = new NotifyIcon()
            {
                Text = "JnnBoost",
                Icon = this.Icon ?? SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = false
            };
            trayIcon.DoubleClick += (s, e) => MostrarJanela();
        }

        private void MostrarJanela()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            trayIcon.Visible = false;
        }

        private void FecharApp()
        {
            if (timerResolutionAtiva) { timeEndPeriod(1); timerResolutionAtiva = false; }
            trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.Visible = true;
                trayIcon.ShowBalloonTip(2000, "JnnBoost",
                    "Minimizado para a bandeja. Clique duas vezes para reabrir.",
                    ToolTipIcon.Info);
            }
            else
            {
                if (timerResolutionAtiva) { timeEndPeriod(1); timerResolutionAtiva = false; }
                base.OnFormClosing(e);
            }
        }

        // -----------------------------------------------
        // PROGRESSO E STATUS
        // -----------------------------------------------
        private void SetProgresso(int valor, string status = "")
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.Invoke(new Action<int, string>(SetProgresso), valor, status);
                return;
            }
            progressBar1.Value = Math.Clamp(valor, 0, 100);
            labelStatus.Text = status;
            // se o botão Otimizar RAM foi pressionado recentemente, reflete no status
            if (lastOptimizeRamPress.HasValue && (DateTime.Now - lastOptimizeRamPress.Value).TotalSeconds < 5)
            {
                labelStatus.Text = "Otimizar RAM executado";
            }
        }

        private void SetBotoes(bool habilitado)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<bool>(SetBotoes), habilitado);
                return;
            }
            foreach (var b in new[] { button1, button2, button3, button4,
                                      button5, button6, button7, button8,
                                      button9, button10 })
                b.Enabled = habilitado;
        }

        private void Notificar(string titulo, string msg, ToolTipIcon icone = ToolTipIcon.Info)
        {
            if (trayIcon.Visible)
                trayIcon.ShowBalloonTip(3000, titulo, msg, icone);
            else
                Log(msg, icone == ToolTipIcon.Error ? CorErro : CorSucesso);
        }

        // inline notifications were removed; Notificar logs when tray not visible

        // -----------------------------------------------
        // MONITOR CPU E RAM
        // -----------------------------------------------
        private void UpdateMonitor(object? sender, EventArgs e)
        {
            try
            {
                float cpu = cpuCounter.NextValue();
                float ram = GetMemoryUsagePercent();
                label1.Text = $"CPU: {cpu:0}%   RAM: {ram:0}%";
                trayIcon.Text = $"JnnBoost | CPU: {cpu:0}% | RAM: {ram:0}%";
            }
            catch (Exception ex) { Log($"Erro monitor: {ex.Message}", CorErro); }
        }

        // -----------------------------------------------
        // UTILITÁRIOS
        // -----------------------------------------------
        private void RunCmd(string args)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process.Start(psi)?.WaitForExit(3000);
        }

        private void RunReg(string args)
        {
            var psi = new ProcessStartInfo("reg.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(3000);
        }

        private void RunPS(string command)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NonInteractive -NoProfile -Command \"{command}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process.Start(psi)?.WaitForExit(5000);
        }

        // -----------------------------------------------
        // LOG COLORIDO
        // -----------------------------------------------
        private void Log(string msg, System.Drawing.Color? cor = null)
        {
            if (textBoxLog.InvokeRequired)
            {
                textBoxLog.Invoke(new Action<string, System.Drawing.Color?>(Log), msg, cor);
                return;
            }

            System.Drawing.Color c = cor ?? CorTexto;
            if (cor == null)
            {
                string u = msg.ToUpper();
                if (u.Contains("PROBLEMA") || u.Contains("CRÍTICO") ||
                    u.Contains("GARGALO") || u.Contains("ERRO")) c = CorErro;
                else if (u.Contains("SUCESSO") || u.Contains("APLICADO") ||
                         u.Contains("OK") || u.Contains("CORRIGIDO") ||
                         u.Contains("LIMPO") || u.Contains("ATIVADO") ||
                         u.Contains("OTIMIZADO")) c = CorSucesso;
                else if (msg.Contains("===") || msg.Contains("---") ||
                         (msg.Contains("[") && msg.Contains("/") && msg.Contains("]")))
                    c = CorCyan;
                else if (u.Contains("AVISO") || u.Contains("ATENÇÃO") ||
                         u.Contains("REINICIE") || u.Contains("RECOMENDAÇÃO"))
                    c = CorAviso;
                else c = CorTexto;
            }

            textBoxLog.SelectionStart = textBoxLog.TextLength;
            textBoxLog.SelectionLength = 0;
            textBoxLog.SelectionColor = c;
            textBoxLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            textBoxLog.SelectionColor = CorTexto;
            textBoxLog.ScrollToCaret();
        }

        // -----------------------------------------------
        // FPS BOOST
        // -----------------------------------------------
        private void FPSBoost()
        {
            try
            {
                Log("Aplicando FPS Boost...");
                RunReg(@"add HKCU\System\GameConfigStore /v GameDVR_Enabled /t REG_DWORD /d 0 /f");
                RunReg(@"add HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR /v AppCaptureEnabled /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKCU\System\GameConfigStore"" /v GameDVR_FSEBehavior /t REG_DWORD /d 2 /f");
                RunReg(@"add ""HKCU\System\GameConfigStore"" /v GameDVR_HonorUserFSEBehaviorMode /t REG_DWORD /d 1 /f");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\GameBar"" /v AllowAutoGameMode /t REG_DWORD /d 1 /f");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\GameBar"" /v AutoGameModeEnabled /t REG_DWORD /d 1 /f");
                Log("Game DVR desativado e Game Mode ativado.");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR"" /v GameDVR_Enabled /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR"" /v AllowGameDVR /t REG_DWORD /d 0 /f");
                Log("Xbox Game Bar desativado.");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings"" /v NOC_GLOBAL_SETTING_TOASTS_ENABLED /t REG_DWORD /d 0 /f");
                Log("Notificações bloqueadas durante jogos.");
                if (!timerResolutionAtiva)
                {
                    if (timeBeginPeriod(1) == 0)
                    {
                        timerResolutionAtiva = true;
                        Log("Timer de resolução 1ms ativado — latência reduzida.");
                    }
                    else Log("AVISO: Não foi possível ativar timer de 1ms.", CorAviso);
                }
                else Log("Timer de resolução 1ms já estava ativo.");
                Log("FPS Boost aplicado com sucesso!");
                Log("Reinicie o jogo para sentir o efeito.");
            }
            catch (Exception ex) { Log($"Erro FPS Boost: {ex.Message}", CorErro); }
        }

        // -----------------------------------------------
        // GPU BOOST
        // -----------------------------------------------
        private void GPUBoost()
        {
            try
            {
                Log("Aplicando GPU Boost...");
                RunReg(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 2 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v Priority /t REG_DWORD /d 6 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Scheduling Category"" /t REG_SZ /d High /f");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences"" /v DirectXUserGlobalSettings /t REG_SZ /d ""VRROptimizeEnable=0;"" /f");
                Log("HAGS e prioridade de GPU configurados.");
                string gpuFabricante = DetectarFabricanteGPU();
                if (gpuFabricante == "NVIDIA") AplicarTweaksNVIDIA();
                else if (gpuFabricante == "AMD") AplicarTweaksAMD();
                Log("GPU Boost aplicado com sucesso!");
                Log("ATENÇÃO: Reinicie o PC para ativar o HAGS.", CorAviso);
            }
            catch (Exception ex) { Log($"Erro GPU Boost: {ex.Message}", CorErro); }
        }

        // -----------------------------------------------
        // OTIMIZAR RAM
        // -----------------------------------------------
        private void OptimizeRAM()
        {
            // marca que o botão foi pressionado (útil para diagnóstico/telemetria local)
            lastOptimizeRamPress = DateTime.Now;
            Log($"Botão Otimizar RAM pressionado em {lastOptimizeRamPress:yyyy-MM-dd HH:mm:ss}");
            Log("Otimizando RAM (ação inicial)...");
            int encerrados = 0;
            foreach (var nome in ProcessosParasitas)
                foreach (var proc in Process.GetProcessesByName(nome))
                    try { proc.Kill(); encerrados++; Log($"Encerrado: {nome}"); } catch { }
            RunCmd("sc stop WidgetService");
            RunCmd("sc config WidgetService start= disabled");
            RunReg(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Dsh"" /v AllowNewsAndInterests /t REG_DWORD /d 0 /f");
            RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarDa /t REG_DWORD /d 0 /f");
            Log("Widgets desabilitados permanentemente.");
            foreach (var proc in Process.GetProcesses())
                try { EmptyWorkingSet(proc.Handle); } catch { }
            GC.Collect(); GC.WaitForPendingFinalizers();
            Log($"RAM otimizada! {encerrados} processo(s) encerrado(s).");

            // Toggle monitor: se estiver ativo, para; se não, inicia monitor em background
            lock (ramMonitorLock)
            {
                if (ramMonitorActive)
                {
                    ramMonitorCts?.Cancel(); ramMonitorCts = null; ramMonitorActive = false;
                    Log("Monitor de RAM parado.");
                    // Evita mostrar notificações que possam roubar foco de jogos em tela cheia
                    Log("Monitor de RAM desativado (sem notificação de sistema).");
                    // animate toggle off
                    button3AnimTarget = false; button3AnimTimer.Start();
                }
                else
                {
                    ramMonitorCts = new CancellationTokenSource();
                    _ = Task.Run(() => RamMonitorLoop(ramMonitorCts.Token));
                    ramMonitorActive = true;
                    Log("Monitor de RAM iniciado.");
                    // Evita mostrar notificações que possam roubar foco de jogos em tela cheia
                    Log("Monitor de RAM ativado (sem notificação de sistema).");
                    // animate toggle on
                    button3AnimTarget = true; button3AnimTimer.Start();
                }
            }
        }

        private async Task RamMonitorLoop(CancellationToken ct)
        {
            try
            {
                var info = new ComputerInfo();
                while (!ct.IsCancellationRequested)
                {
                    long total = (long)(info.TotalPhysicalMemory);
                    long avail = (long)(info.AvailablePhysicalMemory);
                    double usedPct = (double)(total - avail) / total * 100.0;
                    if (usedPct > 85.0) // limiar configurável
                    {
                        Log($"ALERTA: RAM em {usedPct:0}% — verificando processos...");
                        await HandleHighRamAsync();
                    }
                    await Task.Delay(5000, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"Erro monitor RAM: {ex.Message}", CorErro); }
        }

        private async Task HandleHighRamAsync()
        {
            try
            {
                // Lista processos por uso de RAM
                var procs = Process.GetProcesses()
                    .Where(p => p.Id != Process.GetCurrentProcess().Id)
                    .Select(p => new { Proc = p, Mem = SafeWorkingSet(p) })
                    .Where(x => x.Mem > 0)
                    .OrderByDescending(x => x.Mem)
                    .Take(8)
                    .ToList();

                foreach (var x in procs) Log($"  {x.Proc.ProcessName} (PID {x.Proc.Id}) — {x.Mem / 1024 / 1024} MB");

                // Detecta jogo entre os top processes
                Process? jogo = null;
                foreach (var x in procs)
                {
                    try
                    {
                        if (!EhProcessoNaoJogo(x.Proc.ProcessName) && !string.IsNullOrEmpty(x.Proc.MainWindowTitle))
                        { jogo = x.Proc; break; }
                    }
                    catch { }
                }

                if (jogo != null)
                {
                    Log($"Uso alto de RAM parece ser do jogo: {jogo.ProcessName} (PID {jogo.Id}). Nenhuma ação automática aplicada.");
                    Notificar("JnnBoost", $"Uso alto de RAM detectado — jogo ativo: {jogo.ProcessName}");
                    return;
                }

                // Se chegou aqui, maior uso não é jogo — tenta ações seguras
                int encerrados = 0;
                foreach (var nome in ProcessosParasitas)
                    foreach (var proc in Process.GetProcessesByName(nome))
                        try { proc.Kill(); encerrados++; Log($"Encerrado (parasita): {nome}"); } catch { }

                // Reduz prioridade dos heavy processes (não sistema)
                foreach (var x in procs)
                {
                    try
                    {
                        var p = x.Proc;
                        if (EhProcessoNaoJogo(p.ProcessName)) continue;
                        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; Log($"Reduzida prioridade: {p.ProcessName}"); } catch { }
                        try { EmptyWorkingSet(p.Handle); } catch { }
                    }
                    catch { }
                }

                GC.Collect(); GC.WaitForPendingFinalizers();
                Log($"Ações automatizadas executadas. {encerrados} parasita(s) encerrado(s). RAM liberada parcialmente.");
                Notificar("JnnBoost", $"Uso alto de RAM detectado. {encerrados} parasita(s) encerrado(s).");
            }
            catch (Exception ex) { Log($"Erro handle RAM: {ex.Message}", CorErro); }
        }

        private long SafeWorkingSet(Process p)
        {
            try { return p.WorkingSet64; } catch { return 0; }
        }


        // -----------------------------------------------
        // LIMPAR TEMP
        // -----------------------------------------------
        private void CleanTemp()
        {
            try
            {
                Log("Limpando arquivos temporários...");
                int deletados = 0;
                long espacoLiberado = 0;

                // Pastas padrão: TEMP do usuário, Windows Temp e LocalAppData\Temp
                string[] pastas = {
                    Path.GetTempPath(),
                    @"C:\Windows\Temp",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
                };

                foreach (var pasta in pastas)
                {
                    if (!Directory.Exists(pasta)) continue;

                    // Deleta arquivos recursivamente e tenta remover subpastas
                    try
                    {
                        foreach (var f in Directory.GetFiles(pasta, "*", SearchOption.AllDirectories))
                        {
                            try { var fi = new FileInfo(f); espacoLiberado += fi.Length; File.Delete(f); deletados++; }
                            catch { }
                        }
                        foreach (var d in Directory.GetDirectories(pasta))
                        {
                            try { Directory.Delete(d, true); } catch { }
                        }
                    }
                    catch { }
                }

                // Limpa Prefetch (arquivos no diretório). Pode requerer privilégios elevados.
                string prefetch = @"C:\Windows\Prefetch";
                if (Directory.Exists(prefetch))
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(prefetch, "*", SearchOption.AllDirectories))
                        {
                            try { var fi = new FileInfo(f); espacoLiberado += fi.Length; File.Delete(f); deletados++; }
                            catch { }
                        }
                    }
                    catch { }
                }

                double mb = espacoLiberado / 1024.0 / 1024.0;
                Log($"TEMP limpo! {deletados} arquivo(s) removido(s).");
                Log($"Espaço liberado: {mb:0.00} MB");
            }
            catch (Exception ex) { Log($"Erro TEMP: {ex.Message}", CorErro); }
        }

        // -----------------------------------------------
        // LIMPAR REDE
        // -----------------------------------------------
        private void CleanNetwork()
        {
            try
            {
                Log("Otimizando rede...");
                RunCmd("ipconfig /flushdns"); Log("DNS limpo.");
                RunCmd("netsh int tcp set global autotuninglevel=normal");
                RunCmd("netsh int tcp set global chimney=disabled");
                RunCmd("netsh int tcp set global dca=enabled");
                RunCmd("netsh int tcp set global netdma=enabled");
                RunCmd("netsh int tcp set global ecncapability=disabled");
                Log("TCP otimizado.");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f");
                Log("Network throttling desativado.");
                Log("Rede otimizada com sucesso!");
                Log("AVISO: Winsock não resetado — VPNs preservadas.", CorAviso);
            }
            catch (Exception ex) { Log($"Erro rede: {ex.Message}", CorErro); }
        }

        // -----------------------------------------------
        // DIAGNÓSTICO COMPLETO
        // -----------------------------------------------
        private async void DiagnosticoCompleto()
        {
            SetBotoes(false); SetProgresso(0, "Iniciando diagnóstico...");
            int problemas = 0, correcoes = 0;
            try
            {
                Log("=== DIAGNÓSTICO COMPLETO INICIADO ===");

                // [1/6] CPU
                Log("--- [1/6] Analisando CPU ---");
                SetProgresso(5, "Analisando CPU..."); await Task.Delay(200);
                try
                {
                    var s = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    foreach (ManagementObject obj in s.Get())
                    {
                        string nome = obj["Name"]?.ToString()?.Trim() ?? "?";
                        int nucleos = Convert.ToInt32(obj["NumberOfCores"]);
                        int threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                        uint clock = Convert.ToUInt32(obj["MaxClockSpeed"]);
                        uint load = Convert.ToUInt32(obj["LoadPercentage"]);
                        Log($"CPU: {nome}");
                        Log($"Núcleos: {nucleos} físicos / {threads} lógicos");
                        Log($"Clock: {clock} MHz ({clock / 1000.0:0.0} GHz) | Uso: {load}%");
                        if (load > 80)
                        {
                            Log("PROBLEMA: CPU com uso alto em idle!", CorErro); problemas++;
                            var top = Process.GetProcesses().Where(p => !EhProcessoNaoJogo(p.ProcessName))
                                .OrderByDescending(p => p.TotalProcessorTime).Take(3);
                            foreach (var p in top) try { Log($"  Alto uso: {p.ProcessName} (PID {p.Id})"); } catch { }
                            RunCmd("sc stop SysMain"); RunCmd("sc stop DiagTrack");
                            Log("Serviços pesados desativados.", CorSucesso); correcoes++;
                        }
                        else Log("CPU: uso normal. OK.");
                        if (nucleos < 4)
                        {
                            Log("AVISO: CPU com menos de 4 núcleos.", CorAviso); problemas++;
                            RunReg(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583"" /v ValueMax /t REG_DWORD /d 0 /f");
                            Log("Core Parking desativado.", CorSucesso); correcoes++;
                        }
                    }
                }
                catch (Exception ex) { Log($"Erro CPU: {ex.Message}", CorErro); }

                // [2/6] TEMPERATURA
                Log("--- [2/6] Verificando temperatura ---");
                SetProgresso(18, "Verificando temperatura..."); await Task.Delay(200);
                try
                {
                    var ts = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                    bool found = false;
                    foreach (ManagementObject obj in ts.Get())
                    {
                        double k = Convert.ToDouble(obj["CurrentTemperature"]);
                        double c = (k / 10.0) - 273.15;
                        if (c <= 0 || c >= 150) continue;
                        found = true; Log($"Temperatura CPU: {c:0.0}°C");
                        if (c > 90)
                        {
                            Log("PROBLEMA CRÍTICO: CPU superaquecendo!", CorErro);
                            Log("Recomendação: limpar cooler e trocar pasta térmica.", CorAviso);
                            problemas++; RunCmd("powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
                            Log("Plano Balanceado ativado para reduzir temperatura.", CorSucesso); correcoes++;
                        }
                        else if (c > 75) { Log("AVISO: Temperatura elevada. Melhore o fluxo de ar.", CorAviso); problemas++; }
                        else Log("Temperatura CPU: normal. OK.");
                    }
                    if (!found) Log("Temperatura: leitura não disponível neste sistema.");
                }
                catch { Log("Temperatura: leitura não disponível neste sistema."); }

                // [3/6] RAM
                Log("--- [3/6] Analisando RAM ---");
                SetProgresso(33, "Analisando RAM..."); await Task.Delay(200);
                try
                {
                    var info = new ComputerInfo();
                    long totalMb = (long)(info.TotalPhysicalMemory / 1024 / 1024);
                    long livreMb = (long)(info.AvailablePhysicalMemory / 1024 / 1024);
                    long usadoMb = totalMb - livreMb;
                    float uso = (float)usadoMb / totalMb * 100;
                    Log($"RAM: {totalMb} MB total | {usadoMb} MB usados ({uso:0}%) | {livreMb} MB livres");
                    var rs = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                    int pentes = 0;
                    foreach (ManagementObject obj in rs.Get())
                    {
                        pentes++; uint speed = 0;
                        try { speed = Convert.ToUInt32(obj["Speed"]); } catch { }
                        long cap = Convert.ToInt64(obj["Capacity"]) / 1024 / 1024;
                        Log($"  Pente {pentes}: {cap} MB @ {speed} MHz ({obj["BankLabel"]})");
                        if (speed > 0 && speed < 2400) { Log($"AVISO: RAM lenta ({speed} MHz).", CorAviso); problemas++; }
                    }
                    if (totalMb < 8192) { Log("PROBLEMA: Menos de 8 GB de RAM.", CorErro); problemas++; }
                    if (uso > 85)
                    {
                        Log("PROBLEMA: RAM quase cheia!", CorErro); problemas++;
                        int enc = 0;
                        foreach (var nome in ProcessosParasitas)
                            foreach (var proc in Process.GetProcessesByName(nome))
                                try { proc.Kill(); enc++; } catch { }
                        foreach (var proc in Process.GetProcesses()) try { EmptyWorkingSet(proc.Handle); } catch { }
                        Log($"RAM liberada. {enc} processo(s) encerrado(s).", CorSucesso); correcoes++;
                    }
                    else Log("RAM: uso normal. OK.");
                }
                catch (Exception ex) { Log($"Erro RAM: {ex.Message}", CorErro); }

                // [4/6] DISCO
                Log("--- [4/6] Analisando disco ---");
                SetProgresso(50, "Analisando disco..."); await Task.Delay(200);
                try
                {
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    {
                        long totalGb = drive.TotalSize / 1024 / 1024 / 1024;
                        long livreGb = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                        float usoD = (float)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100;
                        Log($"Disco {drive.Name}: {totalGb} GB | {livreGb} GB livres ({usoD:0}% usado)");
                        if (livreGb < 10)
                        {
                            Log($"PROBLEMA: Disco {drive.Name} com menos de 10 GB livre!", CorErro); problemas++;
                            int del = 0;
                            foreach (var f in Directory.GetFiles(Path.GetTempPath()))
                                try { File.Delete(f); del++; } catch { }
                            Log($"{del} arquivo(s) temp removido(s).", CorSucesso); correcoes++;
                        }
                        else if (livreGb < 20) { Log($"AVISO: Disco {drive.Name} com pouco espaço.", CorAviso); problemas++; }
                        else Log($"Disco {drive.Name}: espaço OK.");
                    }
                    var ds = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                    foreach (ManagementObject obj in ds.Get())
                    {
                        string modelo = obj["Model"]?.ToString() ?? "?";
                        ulong bytes = Convert.ToUInt64(obj["Size"]);
                        bool isSSD = modelo.ToUpper().Contains("SSD") || modelo.ToUpper().Contains("NVME") ||
                                        modelo.ToUpper().Contains("M.2") || modelo.ToUpper().Contains("SOLID");
                        Log($"Unidade: {modelo} ({bytes / 1024 / 1024 / 1024} GB) — {(isSSD ? "SSD" : "HDD")}");
                        if (!isSSD)
                        {
                            Log("AVISO: HDD detectado. Jogos carregam mais devagar.", CorAviso);
                            Log("Recomendação: migrar o jogo para um SSD.", CorAviso); problemas++;
                        }
                        else Log("SSD detectado. OK.");
                    }
                }
                catch (Exception ex) { Log($"Erro disco: {ex.Message}", CorErro); }

                // [5/6] GPU
                Log("--- [5/6] Analisando GPU ---");
                SetProgresso(68, "Analisando GPU..."); await Task.Delay(200);
                try
                {
                    long vramReal = ObterVramReal();
                    var gs = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (ManagementObject obj in gs.Get())
                    {
                        string nome = obj["Name"]?.ToString() ?? "?";
                        string driver = obj["DriverVersion"]?.ToString() ?? "?";
                        string status = obj["Status"]?.ToString() ?? "?";
                        long vramMb = vramReal > 0 ? vramReal : (long)(Convert.ToUInt64(obj["AdapterRAM"]) / 1024 / 1024);
                        Log($"GPU: {nome}"); Log($"Driver: {driver} | Status: {status}");
                        Log($"VRAM: {vramMb} MB ({vramMb / 1024.0:0.0} GB)");
                        if (status != "OK") { Log($"PROBLEMA: GPU status '{status}'!", CorErro); problemas++; }
                        else Log("GPU: status OK.");
                        if (vramMb > 0 && vramMb < 2048) { Log("AVISO: Menos de 2 GB de VRAM.", CorAviso); problemas++; }
                        RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f");
                        RunReg(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 2 /f");
                        bool isNv = nome.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isAm = nome.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || nome.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isNv) AplicarTweaksNVIDIA(); else if (isAm) AplicarTweaksAMD();
                        Log("Tweaks de GPU aplicados.", CorSucesso);
                    }
                }
                catch (Exception ex) { Log($"Erro GPU: {ex.Message}", CorErro); }

                // [6/6] REDE
                Log("--- [6/6] Analisando rede ---");
                SetProgresso(83, "Testando rede..."); await Task.Delay(200);
                try
                {
                    var ping = new System.Net.NetworkInformation.Ping();
                    long total = 0; int perdas = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            var r = ping.Send("8.8.8.8", 1000);
                            if (r.Status == System.Net.NetworkInformation.IPStatus.Success)
                            { total += r.RoundtripTime; Log($"  Ping {i + 1}: {r.RoundtripTime}ms"); }
                            else { perdas++; Log($"  Ping {i + 1}: timeout", CorErro); }
                        }
                        catch { perdas++; }
                        await Task.Delay(300);
                    }
                    long media = 5 > perdas ? total / (5 - perdas) : 999;
                    float perda = (float)perdas / 5 * 100;
                    Log($"Latência média: {media}ms | Perda: {perda:0}%");
                    if (perda > 20)
                    {
                        Log("PROBLEMA: Alta perda de pacotes!", CorErro); problemas++;
                        RunCmd("ipconfig /flushdns"); Log("DNS resetado.", CorSucesso); correcoes++;
                    }
                    else if (media > 100)
                    {
                        Log("AVISO: Latência alta. Use cabo ethernet.", CorAviso); problemas++;
                        RunCmd("netsh int tcp set global autotuninglevel=normal"); correcoes++;
                    }
                    else Log("Rede: latência normal. OK.");
                    var ns = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE NetEnabled = True");
                    foreach (ManagementObject obj in ns.Get())
                    {
                        string n = obj["Name"]?.ToString() ?? "";
                        if (n.ToLower().Contains("wi-fi") || n.ToLower().Contains("wireless"))
                        { Log("AVISO: Wi-Fi detectado. Cabo ethernet é mais estável.", CorAviso); problemas++; }
                    }
                }
                catch (Exception ex) { Log($"Erro rede: {ex.Message}", CorErro); }

                SetProgresso(100, "Diagnóstico concluído!");
                Log("=== RELATÓRIO FINAL ===");
                Log($"Problemas encontrados: {problemas} | Correções aplicadas: {correcoes}");
                if (problemas == 0) { Log("Sistema em ótimas condições para jogos!"); Notificar("JnnBoost — Diagnóstico", "Nenhum problema encontrado!"); }
                else if (problemas <= 3) { Log($"{correcoes} corrigido(s). {problemas - correcoes} manual(is).", CorAviso); Notificar("JnnBoost", $"{problemas} problema(s). {correcoes} corrigido(s).", ToolTipIcon.Warning); }
                else { Log("Vários problemas. Veja o log.", CorErro); Notificar("JnnBoost", $"{problemas} problemas!", ToolTipIcon.Error); }
                Log("=== DIAGNÓSTICO CONCLUÍDO ===");
            }
            catch (Exception ex) { Log($"Erro diagnóstico: {ex.Message}", CorErro); }
            finally { SetBotoes(true); await Task.Delay(2000); SetProgresso(0, ""); }
        }

        // -----------------------------------------------
        // RESTAURAR PADRÕES
        // -----------------------------------------------
        private async void RestaurarPadroes()
        {
            var confirm = MessageBox.Show(
                "Isso vai desfazer todas as otimizações e restaurar\n" +
                "o Windows para as configurações padrão.\n\nDeseja continuar?",
                "Restaurar Padrões", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            SetBotoes(false); SetProgresso(0, "Restaurando padrões...");
            try
            {
                Log("=== Restaurando configurações padrão ===");
                SetProgresso(10); RunReg(@"add HKCU\System\GameConfigStore /v GameDVR_Enabled /t REG_DWORD /d 1 /f");
                RunReg(@"add HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR /v AppCaptureEnabled /t REG_DWORD /d 1 /f");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\GameBar"" /v AllowAutoGameMode /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR"" /v AllowGameDVR /t REG_DWORD /d 1 /f");
                Log("Game DVR e Game Bar restaurados.");
                SetProgresso(20); RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings"" /v NOC_GLOBAL_SETTING_TOASTS_ENABLED /t REG_DWORD /d 1 /f");
                Log("Notificações restauradas.");
                SetProgresso(30);
                if (timerResolutionAtiva) { timeEndPeriod(1); timerResolutionAtiva = false; Log("Timer de resolução restaurado."); }
                SetProgresso(40); RunReg(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 1 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 2 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v Priority /t REG_DWORD /d 2 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Scheduling Category"" /t REG_SZ /d Medium /f");
                Log("Configurações de GPU restauradas.");
                SetProgresso(50); RunCmd("powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e"); Log("Plano Balanceado restaurado.");
                SetProgresso(60); RunCmd("sc start SysMain"); RunCmd("sc config SysMain start= auto");
                RunCmd("sc start WSearch"); RunCmd("sc config WSearch start= auto"); Log("SysMain e WSearch reativados.");
                SetProgresso(70); RunCmd("sc config WidgetService start= auto"); RunCmd("sc start WidgetService");
                RunReg(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Dsh"" /v AllowNewsAndInterests /t REG_DWORD /d 1 /f");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarDa /t REG_DWORD /d 1 /f");
                Log("Widgets reativados.");
                SetProgresso(80); RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d 20 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NetworkThrottlingIndex /t REG_DWORD /d 10 /f");
                Log("Perfil multimídia restaurado.");
                SetProgresso(90); RunReg(@"add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"" /v VisualFXSetting /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"" /v EnableTransparency /t REG_DWORD /d 1 /f");
                Log("Efeitos visuais restaurados.");
                await Task.Delay(300);
                SetProgresso(100, "Padrões restaurados!"); Log("=== Padrões restaurados com sucesso! ===");
                Notificar("JnnBoost", "Padrões restaurados!\nReinicie o PC para efeito completo.");
            }
            catch (Exception ex) { Log($"Erro ao restaurar: {ex.Message}", CorErro); }
            finally { SetBotoes(true); await Task.Delay(2000); SetProgresso(0, ""); }
        }

        private void SetProgresso(int valor) => SetProgresso(valor, labelStatus.Text);

        // -----------------------------------------------
        // GPU — DETECÇÃO E TWEAKS
        // -----------------------------------------------
        private string DetectarFabricanteGPU()
        {
            try
            {
                // Prefer DXGI enumeration (more reliable for discrete adapters)
                try
                {
                    var dx = ObterGPUDetalhada();
                    if (!string.IsNullOrEmpty(dx.nome))
                    {
                        var name = dx.nome;
                        if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return "NVIDIA";
                        if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("RADEON", StringComparison.OrdinalIgnoreCase) >= 0) return "AMD";
                        if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) return "Intel";
                    }
                }
                catch { }

                var s = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (ManagementObject obj in s.Get())
                {
                    string n = obj["Name"]?.ToString() ?? "";
                    if (n.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return "NVIDIA";
                    if (n.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) return "AMD";
                    if (n.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) return "Intel";
                }
            }
            catch { }
            return "Desconhecida";
        }

        // Tenta obter nome e VRAM da GPU diretamente via DXGI (mais confiável para placas discretas)
        private (string nome, long vramMb) ObterGPUDetalhada()
        {
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                string bestName = "";
                ulong bestMem = 0;
                for (int i = 0; ; i++)
                {
                    try
                    {
                        factory.EnumAdapters1(i, out IDXGIAdapter1? adapter);
                        if (adapter == null) break;
                        var desc = adapter.Description1;
                        // ignora adapters de software
                        if ((desc.Flags & AdapterFlags.Software) != AdapterFlags.None)
                        {
                            adapter.Dispose();
                            continue;
                        }
                        // desc.DedicatedVideoMemory may be SharpGen.Runtime.PointerSize;
                        // convert explicitly to ulong for comparison to avoid ambiguous operator
                        try
                        {
                            // Use string conversion as a robust fallback across SharpGen versions
                            ulong thisMem = 0;
                            try { thisMem = Convert.ToUInt64(desc.DedicatedVideoMemory.ToString()); }
                            catch { try { thisMem = Convert.ToUInt64(desc.DedicatedVideoMemory); } catch { /* ignore */ } }
                            if (thisMem > bestMem)
                            {
                                bestMem = thisMem;
                                bestName = desc.Description?.Trim() ?? "";
                            }
                        }
                        catch
                        {
                            // ignore adapter if we cannot read memory size
                        }
                        adapter.Dispose();
                    }
                    catch
                    {
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(bestName) && bestMem > 0)
                    return (bestName, (long)(bestMem / 1024 / 1024));
            }
            catch { }
            return ("", 0);
        }

        private void AplicarTweaksNVIDIA()
        {
            try
            {
                Log("Aplicando tweaks NVIDIA...");
                RunReg(@"add ""HKCU\SOFTWARE\NVIDIA Corporation\NVControlPanel2\Client"" /v OptInOrOutPreference /t REG_DWORD /d 0 /f");
                Log("Tweaks NVIDIA aplicados.");
            }
            catch (Exception ex) { Log($"Tweaks NVIDIA: {ex.Message}", CorErro); }
        }

        private void AplicarTweaksAMD()
        {
            try
            {
                Log("Aplicando tweaks AMD/Radeon...");
                RunReg(@"add ""HKCU\SOFTWARE\AMD\CN"" /v OverlayNotificationEnabled /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKCU\SOFTWARE\AMD\CN"" /v ChillEnabled /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKCU\SOFTWARE\AMD\CN"" /v EnhancedSyncEnabled /t REG_DWORD /d 0 /f");
                Log("Tweaks AMD aplicados.");
            }
            catch (Exception ex) { Log($"Tweaks AMD: {ex.Message}", CorErro); }
        }

        // -----------------------------------------------
        // VRAM REAL — contorna limite de 32 bits do WMI
        // -----------------------------------------------
        private long ObterVramReal()
        {
            // Tenta nvidia-smi primeiro (mais preciso para NVIDIA)
            try
            {
                string smi = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
                if (!File.Exists(smi)) smi = "nvidia-smi.exe";
                var psi = new ProcessStartInfo(smi,
                    "--query-gpu=memory.total --format=csv,noheader,nounits")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                    if (long.TryParse(output.Split('\n')[0].Trim(), out long vramMb) && vramMb > 0)
                        return vramMb;
                }
            }
            catch { }

            // Fallback: PowerShell via DXGI (funciona para AMD e Intel também)
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"" +
                    "(Get-WmiObject Win32_VideoController | " +
                    "Where-Object {$_.Name -notlike '*Microsoft*'} | " +
                    "Select-Object -First 1).AdapterRAM / 1MB\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(5000);
                    if (double.TryParse(output.Replace(",", "."),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double mb) && mb > 0)
                        return (long)mb;
                }
            }
            catch { }

            return 0;
        }

        // -----------------------------------------------
        // USO GPU VIA NVIDIA-SMI — fallback quando Performance Counter falha
        // -----------------------------------------------
        private float ObterUsoGpuNvidiaSmi()
        {
            try
            {
                string smi = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
                if (!File.Exists(smi)) smi = "nvidia-smi.exe";
                var psi = new ProcessStartInfo(smi,
                    "--query-gpu=utilization.gpu --format=csv,noheader,nounits")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(2000);
                    if (float.TryParse(output.Split('\n')[0].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float uso))
                        return uso;
                }
            }
            catch { }
            return 0f;
        }

        // -----------------------------------------------
        // TESTE DE ESTRESSE (CPU e "GPU" aproximado)
        // CPU: executa threads ocupadas com operações matemáticas
        // GPU: executa muitas operações de desenho em um bitmap em loop
        // Essas rotinas são intencionais para forçar carga por alguns segundos
        // -----------------------------------------------
        private Task StressCpuAsync(int segundos)
        {
            return Task.Run(() =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(segundos));
                    int workers = Math.Max(1, Environment.ProcessorCount);
                    var tasks = new System.Collections.Generic.List<Task>();
                    for (int i = 0; i < workers; i++)
                    {
                        tasks.Add(Task.Run(() =>
                        {
                            double x = 0.0001;
                            while (!cts.IsCancellationRequested)
                            {
                                // operações pesadas em ponto flutuante
                                for (int k = 0; k < 10000; k++)
                                {
                                    x += Math.Sqrt(k) * Math.Sin(k) * Math.Cos(k);
                                }
                            }
                        }, cts.Token));
                    }
                    Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(segundos + 1));
                }
                catch { }
            });
        }

        private Task StressGpuAsync(int segundos)
        {
            return Task.Run(() =>
            {
                try
                {
                    var rnd = new Random();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < segundos)
                    {
                        // cria bitmap moderado e desenha muitas formas rapidamente
                        using var bmp = new Bitmap(800, 450);
                        using var g = Graphics.FromImage(bmp);
                        for (int y = 0; y < 45; y++)
                        {
                            for (int x = 0; x < 80; x++)
                            {
                                var c = Color.FromArgb(255, rnd.Next(40, 256), rnd.Next(40, 256), rnd.Next(40, 256));
                                using var b = new SolidBrush(c);
                                g.FillRectangle(b, x * 10, y * 10, 10, 10);
                            }
                        }
                        // força algumas operações adicionais
                        bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        bmp.GetPixel(rnd.Next(bmp.Width), rnd.Next(bmp.Height));
                    }
                }
                catch { }
            });
        }

        // Multi-threaded GPU stress: prefer Direct3D path for real GPU load,
        // fallback to GDI if Direct3D fails.
        private Task StressGpuMultiThreadedAsync(int segundos)
        {
            return Task.Run(() =>
            {
                // First try a Direct3D11-based stress which creates large
                // textures and clears them repeatedly to force GPU work.
                try
                {
                    using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                    factory.EnumAdapters1(0, out IDXGIAdapter1? adapter);

                    var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
                    var flags = DeviceCreationFlags.BgraSupport;
                    // D3D11CreateDevice returns (device, immediateContext) in the Vortice.Direct3D11 API
                    var hr = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, flags, featureLevels, out ID3D11Device? device, out ID3D11DeviceContext? context);
                    if (hr.Success && device != null && context != null)
                    {
                        var ctx = context;
                        var sw = Stopwatch.StartNew();
                        var rnd = new Random();
                        // To create more consistent GPU load we perform repeated
                        // staging uploads + GPU-side copies between large default
                        // textures. This avoids needing shaders and still forces
                        // transfer and copy work on the GPU.
                        const int width = 1920;
                        const int height = 1080;
                        var texDescDefault = new Texture2DDescription
                        {
                            Width = width,
                            Height = height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Vortice.DXGI.Format.R8G8B8A8_UNorm,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Default,
                            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                            CPUAccessFlags = CpuAccessFlags.None,
                            MiscFlags = ResourceOptionFlags.None
                        };
                        var texA = device.CreateTexture2D(texDescDefault);
                        var texB = device.CreateTexture2D(texDescDefault);

                        var stagingDesc = texDescDefault;
                        stagingDesc.Usage = ResourceUsage.Staging;
                        stagingDesc.BindFlags = BindFlags.None;
                        stagingDesc.CPUAccessFlags = CpuAccessFlags.Write;
                        var staging = device.CreateTexture2D(stagingDesc);

                        // prepare upload buffer (randomized once)
                        int uploadSize = width * height * 4;
                        var upload = new byte[uploadSize];
                        for (int i = 0; i < uploadSize; i++) upload[i] = (byte)rnd.Next(256);

                        while (sw.Elapsed.TotalSeconds < segundos)
                        {
                            try
                            {
                                // map staging, write data, unmap and copy to default
                                ctx.Map(staging, 0, Vortice.Direct3D11.MapMode.Write, Vortice.Direct3D11.MapFlags.None, out var mapped);
                                try { Marshal.Copy(upload, 0, mapped.DataPointer, uploadSize); } catch { }
                                ctx.Unmap(staging, 0);

                                // copy staging -> texA, then perform several GPU-side copies
                                ctx.CopyResource(texA, staging);
                                ctx.CopyResource(texB, texA);
                                ctx.CopyResource(texA, texB);
                                // flush commands to the GPU to ensure execution
                                ctx.Flush();
                            }
                            catch { }
                        }

                        try { texA.Dispose(); texB.Dispose(); staging.Dispose(); } catch { }
                        try { ctx.ClearState(); ctx.Flush(); } catch { }
                        return;
                    }
                }
                catch
                {
                    // ignore and fallback to GDI below
                }

                // Fallback to multi-threaded GDI bitmap work if D3D path failed
                try
                {
                    int threads = Math.Max(1, Environment.ProcessorCount / 2);
                    var tasks = new System.Collections.Generic.List<Task>();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(segundos));
                    for (int t = 0; t < threads; t++)
                    {
                        tasks.Add(Task.Run(() =>
                        {
                            var rnd = new Random();
                            while (!cts.IsCancellationRequested)
                            {
                                try
                                {
                                    using var bmp = new Bitmap(1920 / 4, 1080 / 4);
                                    using var g = Graphics.FromImage(bmp);
                                    for (int y = 0; y < bmp.Height; y += 4)
                                        for (int x = 0; x < bmp.Width; x += 4)
                                            g.FillRectangle(new SolidBrush(Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256))), x, y, 4, 4);
                                    Thread.Sleep(10);
                                }
                                catch { }
                            }
                        }, cts.Token));
                    }
                    Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(segundos + 1));
                }
                catch { }
            });
        }

        // Tenta obter temperatura da GPU: usa nvidia-smi se disponível, senão retorna -1
        private int ObterTemperaturaGpu()
        {
            try
            {
                string smi = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
                if (!File.Exists(smi)) smi = "nvidia-smi.exe";
                var psi = new ProcessStartInfo(smi,
                    "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(2000);
                    if (int.TryParse(output.Split('\n')[0].Trim(), out int temp)) return temp;
                }
            }
            catch { }
            return -1;
        }

        // -----------------------------------------------
        // DETECÇÃO DE JOGO
        // -----------------------------------------------
        private Process? TentarDetectarViaNvidiaSmi(out string metodo)
        {
            metodo = "";
            try
            {
                string smi = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
                if (!File.Exists(smi)) smi = "nvidia-smi.exe";
                var psi = new ProcessStartInfo(smi, "--query-compute-apps=pid,used_memory --format=csv,noheader,nounits")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                var p = Process.Start(psi); if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                int melhorPid = -1; long melhorMem = 0;
                foreach (var linha in output.Split('\n'))
                {
                    var pts = linha.Trim().Split(','); if (pts.Length < 2) continue;
                    if (int.TryParse(pts[0].Trim(), out int pid) &&
                        long.TryParse(pts[1].Trim(), out long mem) && mem > melhorMem)
                    { melhorMem = mem; melhorPid = pid; }
                }
                if (melhorPid > 0)
                {
                    var proc = Process.GetProcessById(melhorPid);
                    if (EhProcessoNaoJogo(proc.ProcessName)) return null;
                    metodo = $"NVIDIA SMI ({melhorMem} MB VRAM)"; return proc;
                }
            }
            catch { }
            return null;
        }

        private async Task<(Process? processo, string metodo)> TentarDetectarViaPerformanceCounter()
        {
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                var usos = new System.Collections.Generic.Dictionary<int, float>();
                foreach (var inst in cat.GetInstanceNames().Where(i => i.Contains("engtype_3D")))
                {
                    try
                    {
                        using var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                        c.NextValue(); await Task.Delay(100); float uso = c.NextValue();
                        var parts = inst.Split('_');
                        for (int j = 0; j < parts.Length - 1; j++)
                            if (parts[j] == "pid" && int.TryParse(parts[j + 1], out int pid))
                            { if (!usos.ContainsKey(pid)) usos[pid] = 0; usos[pid] += uso; break; }
                    }
                    catch { }
                }
                if (usos.Count == 0) return (null, "");
                var maior = usos.OrderByDescending(kv => kv.Value).First();
                if (maior.Value < 5f) return (null, "");
                var proc = Process.GetProcessById(maior.Key);
                if (EhProcessoNaoJogo(proc.ProcessName)) return (null, "");
                var fg = GetForegroundProcess();
                if (fg != null && !EhProcessoNaoJogo(fg.ProcessName) && fg.Id != proc.Id) proc = fg;
                return (proc, $"GPU Engine Counter ({maior.Value:0}% GPU)");
            }
            catch { return (null, ""); }
        }

        private Process? GetForegroundProcess()
        {
            try { GetWindowThreadProcessId(GetForegroundWindow(), out int pid); return Process.GetProcessById(pid); }
            catch { return null; }
        }

        private Process? TentarDetectarPorMemoria(out string metodo)
        {
            metodo = "maior uso de RAM com janela ativa";
            return Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                            p.WorkingSet64 > 200_000_000 && !EhProcessoNaoJogo(p.ProcessName))
                .OrderByDescending(p => p.WorkingSet64).FirstOrDefault();
        }

        // -----------------------------------------------
        // OTIMIZAR JOGO
        // -----------------------------------------------
        private async void OtimizarJogo()
        {
            SetBotoes(false); SetProgresso(0, "Detectando GPU...");
            try
            {
                Log("=== Otimizar Jogo iniciado ===");
                string gpu = DetectarFabricanteGPU(); Log($"GPU detectada: {gpu}");
                Process? jogo = null; string metodo = "";
                SetProgresso(20, "Detectando jogo...");
                if (gpu == "NVIDIA") jogo = TentarDetectarViaNvidiaSmi(out metodo);
                if (jogo == null) { var r = await TentarDetectarViaPerformanceCounter(); jogo = r.processo; metodo = r.metodo; }
                if (jogo == null) jogo = TentarDetectarPorMemoria(out metodo);
                if (jogo == null) { Log("Nenhum jogo detectado."); Log("Certifique-se que o jogo está aberto."); SetProgresso(0, ""); SetBotoes(true); return; }
                Log($"Jogo: {jogo.ProcessName} (via {metodo})");
                Log($"RAM do jogo: {jogo.WorkingSet64 / 1024 / 1024} MB");
                SetProgresso(35, "Elevando prioridade...");
                try { jogo.PriorityClass = ProcessPriorityClass.High; Log("Prioridade elevada para High."); } catch { }
                SetProgresso(45, "Ajustando Discord...");
                foreach (var proc in Process.GetProcesses().Where(p => p.ProcessName.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0))
                    try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; Log("Discord: prioridade reduzida (não encerrado).", CorAviso); } catch { }
                SetProgresso(55, "Encerrando parasitas...");
                int enc = 0;
                foreach (var nome in ProcessosParasitas)
                    foreach (var proc in Process.GetProcessesByName(nome))
                        try { if (proc.Id != jogo.Id) { proc.Kill(); enc++; Log($"Encerrado: {nome}"); } } catch { }
                Log($"{enc} parasita(s) encerrado(s).");
                SetProgresso(65, "Plano de energia...");
                var psi = new ProcessStartInfo("powercfg", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
                var p = Process.Start(psi); p?.WaitForExit(2000);
                if (p?.ExitCode == 0) Log("Plano Ultra Desempenho ativado.");
                else { RunCmd("powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); Log("Plano Alto Desempenho ativado."); }
                RunCmd("sc stop SysMain"); RunCmd("sc stop WSearch"); Log("SysMain e Windows Search pausados.");
                SetProgresso(78, "Liberando RAM...");
                await Task.Delay(300);
                foreach (var proc in Process.GetProcesses())
                    try { if (proc.Id != jogo.Id && proc.Id != Process.GetCurrentProcess().Id && !proc.ProcessName.IndexOf("discord", StringComparison.OrdinalIgnoreCase).Equals(-1) == false) EmptyWorkingSet(proc.Handle); } catch { }
                Log("RAM liberada.");
                SetProgresso(90, "Aplicando tweaks...");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d 0 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v Priority /t REG_DWORD /d 6 /f");
                RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Scheduling Category"" /t REG_SZ /d High /f");
                if (gpu == "NVIDIA") AplicarTweaksNVIDIA(); else if (gpu == "AMD") AplicarTweaksAMD();
                if (!timerResolutionAtiva && timeBeginPeriod(1) == 0) { timerResolutionAtiva = true; Log("Timer de resolução 1ms ativado."); }
                SetProgresso(100, "Jogo otimizado!");
                Log($"=== {jogo.ProcessName} otimizado com sucesso! ===");
                Log("Discord e browsers foram preservados.");
                Notificar("JnnBoost", $"{jogo.ProcessName} otimizado!\nDiscord e browsers preservados.");
            }
            catch (Exception ex) { Log($"Erro Otimizar Jogo: {ex.Message}", CorErro); }
            finally { SetBotoes(true); await Task.Delay(2000); SetProgresso(0, ""); }
        }

        // -----------------------------------------------
        // ANALISAR GARGALO — com scores corretos e VRAM real
        // -----------------------------------------------
        private async void AnalisarGargalo()
        {
            // evita reentrada concorrente que pode causar "analisando" infinito
            if (System.Threading.Interlocked.Exchange(ref analisandoGargaloFlag, 1) == 1)
            {
                Log("Análise já em andamento. Aguarde a conclusão.");
                return;
            }

            SetBotoes(false); SetProgresso(0, "Iniciando análise...");
            try
            {
                Log("=== Análise de Gargalo iniciada ==="); await Task.Delay(300);

                Log("--- Coletando informações do hardware ---");
                SetProgresso(5, "Lendo CPU...");
                string cpuNome = "?"; int cpuNuc = 0; uint cpuClk = 0; int cpuScore = 0;
                try
                {
                    var s = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    foreach (ManagementObject obj in s.Get())
                    {
                        cpuNome = obj["Name"]?.ToString()?.Trim() ?? "?";
                        cpuNuc = Convert.ToInt32(obj["NumberOfCores"]);
                        cpuClk = Convert.ToUInt32(obj["MaxClockSpeed"]);
                        int thr = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                        Log($"CPU detectada: {cpuNome}");
                        Log($"  Núcleos: {cpuNuc} físicos / {thr} lógicos | Clock: {cpuClk} MHz ({cpuClk / 1000.0:0.0} GHz)");
                        cpuScore = CalcularScoreCPU(cpuNome, cpuNuc, cpuClk);
                        Log($"  Score estimado da CPU: {cpuScore} pontos");
                    }
                }
                catch (Exception ex) { Log($"Erro CPU: {ex.Message}", CorErro); }

                SetProgresso(15, "Lendo GPU...");
                string gpuNome = "?"; long gpuVram = 0; int gpuScore = 0;
                try
                {
                    // Usa ObterVramReal para pegar VRAM correta (sem limite de 32 bits)
                    gpuVram = ObterVramReal();

                    // Tenta DXGI para nome/VRAM mais confiáveis (p.ex. RX 7900 GRE)
                    try
                    {
                        var dxInfo = ObterGPUDetalhada();
                        if (!string.IsNullOrEmpty(dxInfo.nome))
                        {
                            gpuNome = dxInfo.nome;
                            if (dxInfo.vramMb > 0) gpuVram = dxInfo.vramMb;
                        }
                    }
                    catch { }

                    var s = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (ManagementObject obj in s.Get())
                    {
                        string n = obj["Name"]?.ToString() ?? "";
                        if (n.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (n.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        // Only override if we don't already have a DXGI-detected name
                        if (string.IsNullOrEmpty(gpuNome) || gpuNome == "?") gpuNome = n;

                        // Fallback para WMI se nvidia-smi não funcionou
                        if (gpuVram <= 0)
                        {
                            ulong vb = 0;
                            try { vb = Convert.ToUInt64(obj["AdapterRAM"]); } catch { }
                            gpuVram = (long)(vb / 1024 / 1024);
                        }

                        Log($"GPU detectada: {gpuNome}");
                        Log($"  VRAM: {gpuVram} MB ({gpuVram / 1024.0:0.0} GB)");
                        gpuScore = CalcularScoreGPU(gpuNome, gpuVram);
                        Log($"  Score estimado da GPU: {gpuScore} pontos");
                        break;
                    }
                }
                catch (Exception ex) { Log($"Erro GPU: {ex.Message}", CorErro); }

                await Task.Delay(200);

                // Medição em tempo real
                Log("--- Medindo uso em tempo real (5 segundos) ---");
                Log("Mantenha o jogo aberto para resultado mais preciso.");
                SetProgresso(20, "Medindo CPU e GPU...");
                // Pergunta ao usuário se deseja forçar estresse para obter leituras mais confiáveis
                try
                {
                    // usa timeout para evitar bloqueio indefinido na confirmação inline
                    var confirmTask = ShowInlineConfirmAsync("Deseja executar um teste de estresse rápido (5s) para forçar uso de CPU/GPU antes da medição?\n\nSim = Forçar carga (recomendado se os valores estiverem estáticos).");
                    bool doStress = false;
                    var completed = await Task.WhenAny(confirmTask, Task.Delay(10000));
                    if (completed == confirmTask)
                    {
                        try { doStress = await confirmTask; } catch { doStress = false; }
                    }
                    else
                    {
                        Log("Confirmação não respondida em 10s. Prosseguindo sem teste de estresse.");
                        doStress = false;
                    }

                    if (doStress)
                    {
                        Log("Executando teste de estresse (CPU + GPU) por 5 segundos...");
                        // Roda CPU e GPU stress em paralelo por 5 segundos
                        await Task.WhenAll(StressCpuAsync(5), StressGpuMultiThreadedAsync(5));
                        Log("Teste de estresse concluído.");
                        // Após o stress, tenta ler temperatura da GPU
                        try
                        {
                            int t = ObterTemperaturaGpu();
                            if (t >= 0) Log($"Temperatura GPU: {t} °C");
                            else Log("Temperatura GPU: leitura não disponível.", CorAviso);
                        }
                        catch { }
                    }
                }
                catch { }
                var cpuAm = new System.Collections.Generic.List<float>();
                var gpuAm = new System.Collections.Generic.List<float>();
                var ramAm = new System.Collections.Generic.List<float>();

                // Para NVIDIA: usa nvidia-smi como fonte primária (mais confiável)
                bool isNvidia = gpuNome.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;

                // Prepara todos os counters de GPU Engine somados
                // Soma TODAS as instâncias para ter o uso total real da GPU
                var todosCounters = new System.Collections.Generic.List<PerformanceCounter>();
                try
                {
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    var instancias = cat.GetInstanceNames();

                    // Pega todas as instâncias que contenham qualquer tipo de engine
                    foreach (var inst in instancias.Where(i =>
                        i.Contains("engtype_3D") ||
                        i.Contains("engtype_Graphics") ||
                        i.Contains("engtype_Copy") ||
                        i.Contains("engtype_VideoDecode") ||
                        i.Contains("engtype_VideoEncode")))
                    {
                        try
                        {
                            var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                            c.NextValue(); // aquecimento
                            todosCounters.Add(c);
                        }
                        catch { }
                    }

                    // Se não achou nenhum engine específico, pega todas as instâncias
                    if (todosCounters.Count == 0)
                    {
                        foreach (var inst in instancias)
                        {
                            try
                            {
                                var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                                c.NextValue();
                                todosCounters.Add(c);
                            }
                            catch { }
                        }
                    }

                    if (todosCounters.Count > 0)
                        await Task.Delay(500); // aguarda estabilizar
                }
                catch { }

                Log($"  {todosCounters.Count} counter(s) de GPU encontrado(s).");

                for (int i = 0; i < 10; i++)
                {
                    float cpu = cpuCounter.NextValue();
                    float gpu = 0;
                    float ramPercent = GetMemoryUsagePercent();

                    // Tenta Performance Counters primeiro (soma todas as instâncias)
                    if (todosCounters.Count > 0)
                    {
                        float soma = 0;
                        int validos = 0;
                        foreach (var c in todosCounters)
                        {
                            try
                            {
                                float v = c.NextValue();
                                if (v > 0) { soma += v; validos++; }
                            }
                            catch { }
                        }
                        // Usa o maior valor encontrado entre as instâncias
                        // (evita somar 100% de cada engine dando >100%)
                        gpu = validos > 0 ? Math.Min(soma, 100f) : 0f;
                    }

                    // Se performance counters não forneceram valor, tenta nvidia-smi para NVIDIA
                    if ((gpu == 0 || gpu < 1f) && isNvidia)
                        gpu = ObterUsoGpuNvidiaSmi();

                    cpuAm.Add(cpu); gpuAm.Add(gpu); ramAm.Add(ramPercent);
                    Log($"  Amostra {i + 1:00}/10 — CPU: {cpu:0}% | GPU: {gpu:0}%");
                    SetProgresso(20 + (i + 1) * 5, $"Medindo... {(i + 1) * 10}%");
                    await Task.Delay(500);
                }

                // Libera counters
                foreach (var c in todosCounters) try { c.Dispose(); } catch { }
                todosCounters.Clear();

                float mCpu = cpuAm.Average();
                float mGpu = gpuAm.Count > 0 ? gpuAm.Average() : 0;
                float xCpu = cpuAm.Max();
                float xGpu = gpuAm.Count > 0 ? gpuAm.Max() : 0;
                Log($"CPU — média: {mCpu:0.0}% | pico: {xCpu:0.0}%");
                Log($"GPU — média: {mGpu:0.0}% | pico: {xGpu:0.0}%");
                if (ramAm.Count > 0)
                {
                    float mRam = ramAm.Average();
                    float xRam = ramAm.Max();
                    Log($"RAM — média: {mRam:0.0}% | pico: {xRam:0.0}%");
                }

                if (mGpu < 5f)
                    Log("AVISO: GPU com uso muito baixo — abra um jogo para análise mais precisa.", CorAviso);

                await Task.Delay(200); SetProgresso(75, "Analisando compatibilidade...");

                // Análise de compatibilidade CPU x GPU
                Log("--- Análise de compatibilidade CPU x GPU ---"); await Task.Delay(300);
                if (cpuScore > 0 && gpuScore > 0)
                {
                    float ratio = (float)gpuScore / cpuScore;
                    Log($"Score CPU: {cpuScore} | Score GPU: {gpuScore} | Razão GPU/CPU: {ratio:0.00}x");

                    if (ratio > 6.0f)
                    {
                        Log("GARGALO SEVERO: GPU muito superior à CPU!", CorErro);
                        Log($"Sua GPU ({gpuNome}) é muito mais", CorErro);
                        Log($"poderosa que sua CPU ({cpuNome}).", CorErro);
                        Log("A CPU não alimenta a GPU com dados suficientes.", CorErro);
                        Log("Perda estimada: 20% a 40% do potencial da GPU.", CorErro);
                        Log("Recomendação: upgrade de CPU ou usar DLSS/FSR.", CorAviso);
                    }
                    else if (ratio > 4.0f)
                    {
                        Log("GARGALO MODERADO: GPU bem superior à CPU.", CorAviso);
                        Log($"GPU ({gpuNome}) pode ser limitada pela", CorAviso);
                        Log($"CPU ({cpuNome}) em jogos pesados.", CorAviso);
                        Log("Perda estimada: 5% a 15% em jogos CPU-intensivos.", CorAviso);
                        Log("Recomendação: usar DLSS/FSR e reduzir simulações.", CorAviso);
                    }
                    else if (ratio < 0.5f)
                    {
                        Log("GARGALO SEVERO: CPU muito superior à GPU!", CorErro);
                        Log($"CPU ({cpuNome}) ociosa esperando", CorErro);
                        Log($"a GPU ({gpuNome}) processar frames.", CorErro);
                        Log("CPU poderosa sendo desperdiçada.", CorErro);
                        Log("Recomendação: upgrade de GPU ou aumentar qualidade gráfica.", CorAviso);
                    }
                    else if (ratio < 0.8f)
                    {
                        Log("GARGALO LEVE: CPU um pouco superior à GPU.", CorAviso);
                        Log("Você pode aumentar qualidade gráfica sem perda de FPS.", CorAviso);
                        Log("Recomendação: aumente resolução ou qualidade de texturas.", CorAviso);
                    }
                    else
                    {
                        Log("Hardware COMPATÍVEL — CPU e GPU bem balanceadas!", CorSucesso);
                        Log($"CPU ({cpuNome}) e GPU ({gpuNome})", CorSucesso);
                        Log("trabalham em harmonia. Ótima combinação!", CorSucesso);
                    }
                }
                else Log("AVISO: Hardware não reconhecido — score não calculado.", CorAviso);

                await Task.Delay(200); SetProgresso(85, "Analisando uso...");

                // Análise de uso em tempo real
                Log("--- Análise do uso em tempo real ---");
                bool gCpu = mCpu > 85f && mGpu < 70f;
                bool gGpu = mGpu > 90f && mCpu < 70f;
                bool ambos = mCpu > 70f && mGpu > 70f;
                bool ocioso = mCpu < 30f && mGpu < 10f;

                if (ocioso)
                {
                    Log("USO: CPU e GPU com uso muito baixo.", CorAviso);
                    Log("Abra o jogo antes de rodar a análise para resultados precisos.");
                }
                else if (gCpu)
                {
                    Log("USO: CPU sobrecarregada com GPU ociosa.", CorErro);
                    Log("CPU não processa frames rápido o suficiente.", CorErro);
                    Log("Sintomas: FPS baixo mesmo com GPU pouco usada.", CorErro);
                }
                else if (gGpu)
                {
                    Log("USO: GPU sobrecarregada com CPU ociosa.", CorErro);
                    Log("GPU não renderiza frames a tempo.", CorErro);
                    Log("Sintomas: FPS baixo, stuttering em áreas detalhadas.", CorErro);
                }
                else if (ambos)
                {
                    Log("USO: CPU e GPU ambas com uso alto — cenário ideal!", CorSucesso);
                }
                else
                {
                    Log($"USO: CPU {mCpu:0}% e GPU {mGpu:0}% — uso moderado.");
                }

                await Task.Delay(200); SetProgresso(90, "Aplicando correções...");

                // Correções automáticas — só aplica se uso real confirmar gargalo
                // Nunca aplica correção baseada em score se GPU está zerada (sem jogo aberto)
                Log("--- Aplicando correções automáticas ---");
                bool gpuComDados = mGpu >= 5f; // só usa score se GPU tem dados reais
                bool corrigirCpu = gCpu || (gpuComDados && cpuScore > 0 && gpuScore > 0 && (float)gpuScore / cpuScore > 6.0f);
                bool corrigirGpu = gGpu || (gpuComDados && cpuScore > 0 && gpuScore > 0 && (float)gpuScore / cpuScore < 0.5f);

                if (corrigirCpu)
                {
                    Log("Aplicando otimizações para aliviar CPU...");
                    RunCmd("sc stop SysMain"); RunCmd("sc stop WSearch");
                    RunCmd("sc stop DiagTrack"); RunCmd("sc stop dmwappushservice");
                    RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d 0 /f");
                    RunCmd("powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61");
                    RunReg(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583"" /v ValueMax /t REG_DWORD /d 0 /f");
                    if (!timerResolutionAtiva && timeBeginPeriod(1) == 0) { timerResolutionAtiva = true; Log("Timer 1ms ativado.", CorSucesso); }
                    Log("Serviços pausados, Ultra Desempenho e Core Parking desativados.", CorSucesso);
                    Notificar("JnnBoost — Gargalo", "Gargalo de CPU corrigido!", ToolTipIcon.Warning);
                }
                else if (corrigirGpu)
                {
                    Log("Aplicando otimizações para maximizar GPU...");
                    RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f");
                    RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Scheduling Category"" /t REG_SZ /d High /f");
                    RunReg(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 2 /f");
                    RunReg(@"add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"" /v VisualFXSetting /t REG_DWORD /d 2 /f");
                    RunReg(@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"" /v EnableTransparency /t REG_DWORD /d 0 /f");
                    Log("GPU priorizada e efeitos visuais desativados.", CorSucesso);
                    Log("ATENÇÃO: Reinicie o PC para ativar o HAGS.", CorAviso);
                    Notificar("JnnBoost — Gargalo", "Gargalo de GPU corrigido!", ToolTipIcon.Warning);
                }
                else
                {
                    RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d 0 /f");
                    RunReg(@"add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f");
                    Log("Tweaks finos de balanceamento aplicados.", CorSucesso);
                }

                SetProgresso(100, "Análise concluída!");
                Log("=== RESUMO DA ANÁLISE ===");
                Log($"CPU: {cpuNome} (Score: {cpuScore})");
                Log($"GPU: {gpuNome} (Score: {gpuScore})");
                if (cpuScore > 0 && gpuScore > 0)
                {
                    float r = (float)gpuScore / cpuScore;
                    string sit = r > 6.0f ? "Gargalo severo de CPU" :
                                 r > 4.0f ? "Gargalo moderado de CPU" :
                                 r < 0.5f ? "Gargalo severo de GPU" :
                                 r < 0.8f ? "Gargalo leve de GPU" : "Hardware balanceado";
                    Log($"Situação: {sit}");
                }
                Log("=== Análise concluída ===");
                Notificar("JnnBoost — Gargalo",
                    cpuScore > 0 && gpuScore > 0
                        ? $"CPU Score: {cpuScore} | GPU Score: {gpuScore}"
                        : "Análise concluída. Veja o log.",
                    (gCpu || gGpu) ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch (Exception ex) { Log($"Erro análise: {ex.Message}", CorErro); }
            finally { SetBotoes(true); await Task.Delay(2000); SetProgresso(0, ""); System.Threading.Interlocked.Exchange(ref analisandoGargaloFlag, 0); }
        }

        // -----------------------------------------------
        // SCORE CPU — valores reais do PassMark Multithread
        // Fonte: cpubenchmark.net (atualizado abril 2026)
        // -----------------------------------------------
        private int CalcularScoreCPU(string nome, int nucleos, uint clockMhz)
        {
            string n = nome.ToUpper();

            // ---- AMD Ryzen 9000 ----
            if (n.Contains("9950X")) return 72000;
            if (n.Contains("9900X")) return 54430;
            if (n.Contains("9700X")) return 37064;
            if (n.Contains("9600X")) return 30000;

            // ---- AMD Ryzen 7000 ----
            if (n.Contains("7950X3D")) return 65000;
            if (n.Contains("7950X")) return 62195;
            if (n.Contains("7900X3D")) return 54000;
            if (n.Contains("7900X")) return 51279;
            if (n.Contains("7900")) return 46000;
            if (n.Contains("7800X3D")) return 34295;
            if (n.Contains("7700X")) return 35547;
            if (n.Contains("7700")) return 34363;
            if (n.Contains("7600X")) return 28500;
            if (n.Contains("7600")) return 26000;

            // ---- AMD Ryzen 5000 ----
            if (n.Contains("5950X")) return 45305;
            if (n.Contains("5900X")) return 38926;
            if (n.Contains("5900")) return 35000;
            if (n.Contains("5800X3D")) return 30000;
            if (n.Contains("5800X")) return 27000;
            if (n.Contains("5800")) return 25000;
            if (n.Contains("5700X")) return 23500;
            if (n.Contains("5700")) return 21000;
            if (n.Contains("5600X")) return 22500;
            if (n.Contains("5600")) return 20000;

            // ---- AMD Ryzen 3000 ----
            if (n.Contains("3950X")) return 29000;
            if (n.Contains("3900X")) return 27000;
            if (n.Contains("3900")) return 25000;
            if (n.Contains("3800X")) return 23500;
            if (n.Contains("3800")) return 22000;
            if (n.Contains("3700X")) return 22409;  // score real PassMark
            if (n.Contains("3700")) return 21000;
            if (n.Contains("3600X")) return 19500;
            if (n.Contains("3600")) return 18500;
            if (n.Contains("3500X")) return 15000;
            if (n.Contains("3500")) return 13500;
            if (n.Contains("3300X")) return 13000;
            if (n.Contains("3100")) return 10500;

            // ---- AMD Ryzen 2000/1000 ----
            if (n.Contains("2700X")) return 14000;
            if (n.Contains("2700")) return 12500;
            if (n.Contains("2600X")) return 12000;
            if (n.Contains("2600")) return 11000;
            if (n.Contains("1800X")) return 11500;
            if (n.Contains("1700X")) return 11000;
            if (n.Contains("1700")) return 10000;
            if (n.Contains("1600X")) return 9500;
            if (n.Contains("1600")) return 9000;

            // ---- Intel Core i9 14ª/13ª geração ----
            if (n.Contains("14900KS") || n.Contains("14900K")) return 58500;
            if (n.Contains("14900KF")) return 58251;
            if (n.Contains("14900")) return 55000;
            if (n.Contains("13900KS")) return 58213;
            if (n.Contains("13900K")) return 57000;
            if (n.Contains("13900KF")) return 56500;
            if (n.Contains("13900")) return 53000;

            // ---- Intel Core i7 14ª/13ª geração ----
            if (n.Contains("14700KF") || n.Contains("14700K")) return 52133;
            if (n.Contains("14700")) return 50000;
            if (n.Contains("13700KF") || n.Contains("13700K")) return 45713;
            if (n.Contains("13700")) return 43000;

            // ---- Intel Core i5 14ª/13ª geração ----
            if (n.Contains("14600KF") || n.Contains("14600K")) return 36614;
            if (n.Contains("14600")) return 34000;
            if (n.Contains("13600KF") || n.Contains("13600K")) return 36000;
            if (n.Contains("13600")) return 33000;
            if (n.Contains("13500")) return 27000;
            if (n.Contains("13400")) return 23000;

            // ---- Intel Core i3 13ª geração ----
            if (n.Contains("13100")) return 13000;

            // ---- Intel Core i9 12ª geração ----
            if (n.Contains("12900KS") || n.Contains("12900K")) return 41140;
            if (n.Contains("12900")) return 38000;

            // ---- Intel Core i7 12ª geração ----
            if (n.Contains("12700KF") || n.Contains("12700K")) return 34000;
            if (n.Contains("12700")) return 32000;

            // ---- Intel Core i5 12ª geração ----
            if (n.Contains("12600KF") || n.Contains("12600K")) return 24500;
            if (n.Contains("12600")) return 22000;
            if (n.Contains("12500")) return 18000;
            if (n.Contains("12400")) return 17000;

            // ---- Intel Core i9/i7/i5 11ª geração ----
            if (n.Contains("11900K")) return 22000;
            if (n.Contains("11900")) return 20000;
            if (n.Contains("11700K")) return 20500;
            if (n.Contains("11700")) return 18000;
            if (n.Contains("11600K")) return 16500;
            if (n.Contains("11600")) return 14000;

            // ---- Intel Core i9/i7/i5 10ª geração ----
            if (n.Contains("10900K")) return 22301;
            if (n.Contains("10900")) return 20000;
            if (n.Contains("10700K")) return 18000;
            if (n.Contains("10700")) return 16000;
            if (n.Contains("10600K")) return 14000;
            if (n.Contains("10600")) return 12000;
            if (n.Contains("10400")) return 11000;

            // ---- Intel Core i9/i7/i5 9ª e 8ª geração ----
            if (n.Contains("9900K")) return 16000;
            if (n.Contains("9700K")) return 13000;
            if (n.Contains("9600K")) return 11000;
            if (n.Contains("8700K")) return 12000;
            if (n.Contains("8700")) return 11000;
            if (n.Contains("8600K")) return 10500;
            if (n.Contains("8400")) return 9500;

            // Fallback por núcleos se não reconheceu
            if (nucleos >= 16) return 35000;
            if (nucleos >= 12) return 28000;
            if (nucleos >= 8) return 22000;
            if (nucleos >= 6) return 16000;
            if (nucleos >= 4) return 10000;
            return 6000;
        }

        // -----------------------------------------------
        // SCORE GPU — valores reais do PassMark
        // Fonte: videocardbenchmark.net (atualizado abril 2026)
        // Na mesma escala do score CPU para comparação correta
        // -----------------------------------------------
        private int CalcularScoreGPU(string nome, long vramMb)
        {
            string n = nome.ToUpper();

            // ---- NVIDIA RTX 50xx ----
            if (n.Contains("5090")) return 39047;
            if (n.Contains("5080")) return 35694;
            if (n.Contains("5070 TI") || n.Contains("5070TI")) return 32427;
            if (n.Contains("5070")) return 28500;
            if (n.Contains("5060 TI") || n.Contains("5060TI")) return 24000;
            if (n.Contains("5060")) return 20802;

            // ---- NVIDIA RTX 40xx ----
            if (n.Contains("4090")) return 38062;
            if (n.Contains("4080 SUPER") || n.Contains("4080S")) return 34265;
            if (n.Contains("4080")) return 34430;
            if (n.Contains("4070 TI SUPER") || n.Contains("4070TIS")) return 31809;
            if (n.Contains("4070 TI") || n.Contains("4070TI")) return 31570;
            if (n.Contains("4070 SUPER") || n.Contains("4070S")) return 29952;
            if (n.Contains("4070")) return 26909;
            if (n.Contains("4060 TI") || n.Contains("4060TI")) return 22611;
            if (n.Contains("4060")) return 19511;

            // ---- NVIDIA RTX 30xx ----
            if (n.Contains("3090 TI") || n.Contains("3090TI")) return 29285;
            if (n.Contains("3090")) return 26546;
            if (n.Contains("3080 TI") || n.Contains("3080TI")) return 26773;
            if (n.Contains("3080")) return 25022;
            if (n.Contains("3070 TI") || n.Contains("3070TI")) return 23250;
            if (n.Contains("3070")) return 22120;
            if (n.Contains("3060 TI") || n.Contains("3060TI")) return 20264;
            if (n.Contains("3060")) return 16740;
            if (n.Contains("3050")) return 13000;

            // ---- NVIDIA RTX 20xx ----
            if (n.Contains("2080 TI") || n.Contains("2080TI")) return 22000;
            if (n.Contains("2080 SUPER") || n.Contains("2080S")) return 19446;
            if (n.Contains("2080")) return 18589;
            if (n.Contains("2070 SUPER") || n.Contains("2070S")) return 18151;
            if (n.Contains("2070")) return 16060;
            if (n.Contains("2060 SUPER") || n.Contains("2060S")) return 15500;
            if (n.Contains("2060")) return 14000;

            // ---- NVIDIA GTX 16xx ----
            if (n.Contains("1660 TI") || n.Contains("1660TI") ||
                n.Contains("1660 SUPER") || n.Contains("1660S")) return 12500;
            if (n.Contains("1660")) return 11000;
            if (n.Contains("1650 SUPER") || n.Contains("1650S")) return 10000;
            if (n.Contains("1650")) return 8000;

            // ---- NVIDIA GTX 10xx ----
            if (n.Contains("1080 TI") || n.Contains("1080TI")) return 18590;
            if (n.Contains("1080")) return 15000;
            if (n.Contains("1070 TI") || n.Contains("1070TI")) return 13500;
            if (n.Contains("1070")) return 12000;
            if (n.Contains("1060")) return 9500;
            if (n.Contains("1050 TI") || n.Contains("1050TI")) return 7000;
            if (n.Contains("1050")) return 5500;

            // ---- AMD RX 9000 ----
            if (n.Contains("9070 XT") || n.Contains("9070XT")) return 26904;
            if (n.Contains("9070")) return 24000;
            if (n.Contains("9060")) return 18447;

            // ---- AMD RX 7000 ----
            if (n.Contains("7900 XTX") || n.Contains("7900XTX")) return 31412;
            if (n.Contains("7900 XT") || n.Contains("7900XT")) return 29035;
            // AMD GRE variants (explicit matches)
            if (n.Contains("7900 GRE XTX") || n.Contains("7900GREGTX") || n.Contains("7900GREXTX")) return 31200;
            if (n.Contains("7900 GRE XT") || n.Contains("7900GREXT") || n.Contains("7900GRE XT")) return 29500;
            if (n.Contains("7900 GRE") || n.Contains("7900GRE")) return 27500;
            if (n.Contains("7900")) return 27000;
            if (n.Contains("7800 GRE") || n.Contains("7800GRE")) return 23500;
            if (n.Contains("7700 GRE") || n.Contains("7700GRE")) return 20000;
            if (n.Contains("7600 GRE") || n.Contains("7600GRE")) return 17000;
            if (n.Contains("7800 XT") || n.Contains("7800XT")) return 24000;
            if (n.Contains("7700 XT") || n.Contains("7700XT")) return 21000;
            if (n.Contains("7700")) return 19000;
            if (n.Contains("7600 XT") || n.Contains("7600XT")) return 18000;
            if (n.Contains("7600")) return 16000;

            // ---- AMD RX 6000 ----
            if (n.Contains("6950 XT") || n.Contains("6950XT")) return 28000;
            if (n.Contains("6900 XT") || n.Contains("6900XT")) return 26000;
            if (n.Contains("6800 XT") || n.Contains("6800XT")) return 25000;
            if (n.Contains("6800")) return 23000;
            if (n.Contains("6750 XT") || n.Contains("6750XT")) return 21000;
            if (n.Contains("6700 XT") || n.Contains("6700XT")) return 19731;
            if (n.Contains("6700")) return 18919;
            if (n.Contains("6650 XT") || n.Contains("6650XT")) return 17500;
            if (n.Contains("6600 XT") || n.Contains("6600XT")) return 16500;
            if (n.Contains("6600")) return 15000;
            if (n.Contains("6500 XT") || n.Contains("6500XT")) return 10000;
            if (n.Contains("6400")) return 8000;

            // ---- AMD RX 5000 ----
            if (n.Contains("5700 XT") || n.Contains("5700XT")) return 16080;
            if (n.Contains("5700")) return 14000;
            if (n.Contains("5600 XT") || n.Contains("5600XT")) return 13000;
            if (n.Contains("5500 XT") || n.Contains("5500XT")) return 10000;

            // ---- Intel Arc ----
            if (n.Contains("B580")) return 17000;
            if (n.Contains("A770")) return 16000;
            if (n.Contains("A750")) return 14000;
            if (n.Contains("A580")) return 11000;
            if (n.Contains("A380")) return 7000;
            if (n.Contains("A310")) return 4500;

            // ---- GPU integrada ----
            if (n.Contains("IRIS") || n.Contains("UHD") ||
                n.Contains("VEGA") || n.Contains("INTEGRATED") ||
                n.Contains("RADEON GRAPHICS")) return 1500;

            // Fallback por VRAM
            if (vramMb >= 24000) return 32000;
            if (vramMb >= 16000) return 26000;
            if (vramMb >= 12000) return 22000;
            if (vramMb >= 8000) return 18000;
            if (vramMb >= 6000) return 13000;
            if (vramMb >= 4000) return 9000;
            return 5000;
        }

        // -----------------------------------------------
        // EXCLUSÃO NO DEFENDER
        // -----------------------------------------------
        private void AdicionarExclusaoDefender()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                RunPS($"Add-MpPreference -ExclusionPath '{exePath}'");
                Log("Exclusão no Windows Defender adicionada.", CorSucesso);
            }
            catch { }
        }

        // -----------------------------------------------
        // FORM LOAD
        // -----------------------------------------------
        private void Form1_Load(object? sender, EventArgs e)
        {
            try
            {
                var stream = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetManifestResourceStream("JnnBoost.icone.ico");
                if (stream != null)
                {
                    this.Icon = new System.Drawing.Icon(stream);
                }
                else
                {
                    string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (ico != null) this.Icon = ico;
                    }
                }
                trayIcon.Icon = this.Icon ?? SystemIcons.Application;
            }
            catch { }

            label1.Text = "CPU: --%   RAM: --%";
            Log("=== JnnBoost iniciado com sucesso ===");
            Log("Rodando como Administrador.");
            Log($"GPU: {DetectarFabricanteGPU()}");
            AdicionarExclusaoDefender();
        }

        // Exibe uma notificação discreta no UI (não rouba foco)
        private void ShowInlineNotification(string text, int ms = 3000)
        {
            try
            {
                if (labelInlineNotification == null) return;
                labelInlineNotification.Text = text;
                labelInlineNotification.Visible = true;
                var t = new System.Windows.Forms.Timer();
                t.Interval = ms;
                t.Tick += (s, e) => { labelInlineNotification.Visible = false; t.Stop(); t.Dispose(); };
                t.Start();
            }
            catch { }
        }

        // Mostrar confirmação inline substitui MessageBox.Show para evitar roubar foco
        private Task<bool> ShowInlineConfirmAsync(string message)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                if (panelConfirm == null) { tcs.SetResult(false); return tcs.Task; }
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        labelConfirmText.Text = message;
                        panelConfirm.BringToFront(); panelConfirm.Visible = true;
                    }));
                }
                else { labelConfirmText.Text = message; panelConfirm.Visible = true; }

                void yesHandler(object? s, EventArgs e) { panelConfirm.Visible = false; btnConfirmYes.Click -= yesHandler; btnConfirmNo.Click -= noHandler; tcs.SetResult(true); }
                void noHandler(object? s, EventArgs e) { panelConfirm.Visible = false; btnConfirmYes.Click -= yesHandler; btnConfirmNo.Click -= noHandler; tcs.SetResult(false); }

                btnConfirmYes.Click += yesHandler;
                btnConfirmNo.Click += noHandler;
            }
            catch { tcs.SetResult(false); }
            return tcs.Task;
        }

        // -----------------------------------------------
        // EVENTOS DOS BOTÕES
        // -----------------------------------------------
        private void button1_Click(object? sender, EventArgs e) { FPSBoost(); }
        private void button2_Click(object? sender, EventArgs e) { GPUBoost(); }
        private void button3_Click(object? sender, EventArgs e) { OptimizeRAM(); }
        private void button4_Click(object? sender, EventArgs e) { CleanTemp(); }
        private void button5_Click(object? sender, EventArgs e) { CleanNetwork(); }
        private void button6_Click(object? sender, EventArgs e) { DiagnosticoCompleto(); }
        private void button7_Click(object? sender, EventArgs e) { OtimizarJogo(); }
        private void button8_Click(object? sender, EventArgs e) { AnalisarGargalo(); }
        private void button9_Click(object? sender, EventArgs e) { RestaurarPadroes(); }
        private void button10_Click(object? sender, EventArgs e)
        {
            try
            {
                if (overlay == null || overlay.IsDisposed)
                { overlay = new OverlayForm(); overlay.Show(); Log("Overlay iniciado."); }
                else
                { overlay.Close(); overlay = null; Log("Overlay fechado."); }
            }
            catch (Exception ex) { Log($"Erro overlay: {ex.Message}", CorErro); }
        }
    }
}