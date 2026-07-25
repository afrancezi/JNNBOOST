using System;
using System.Windows.Forms;

namespace JnnBoost
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Exibe tela de login antes de executar a aplicação principal
            using (var login = new LoginForm())
            {
                var result = login.ShowDialog();
                if (result == DialogResult.OK && login.Autenticado)
                {
                    Application.Run(new Form1());
                }
                else
                {
                    // Não autenticado ou cancelado: encerra a aplicação
                    return;
                }
            }
        }
    }
}
