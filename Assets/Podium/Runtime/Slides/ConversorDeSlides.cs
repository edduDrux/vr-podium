using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace Podium.Slides
{
    /// <summary>
    /// Transforma a apresentação em imagens de página, para que apontar o PDF ou o PPTx
    /// baste — sem pasta preparada à mão.
    /// <para>
    /// O runtime do Unity não decodifica nenhum dos dois formatos: não existe rasterizador
    /// de PDF na engine, e embutir um seria uma dependência nativa por tela. Então alguém
    /// converte, e aqui é a própria máquina de quem apresenta: PPTx pelo PowerPoint
    /// instalado, PDF pelo PyMuPDF do container do backend — a mesma biblioteca que extrai
    /// o texto dos slides, de modo que a página 7 da parede é a página 7 da âncora.
    /// </para>
    /// <para>
    /// Isso vale no Editor e num build de Windows, que é onde o TCC roda hoje. Num headset
    /// não há Docker nem Office: quando a hora chegar, quem renderiza é o backend, servindo
    /// as páginas do arquivo que ele já recebeu no /init. É por isso que a conversão mora
    /// atrás de <see cref="Disponivel"/>, e não espalhada pela cena.
    /// </para>
    /// </summary>
    public static class ConversorDeSlides
    {
        public const string SufixoDaPasta = "_slides";

        private const string ContainerDaApi = "podium_api";
        private const int LarguraDaExportacao = 1920;
        private const int TempoLimiteMs = 180000;

        /// <summary>Onde há PowerPoint e Docker para chamar: Editor e build de Windows.</summary>
        public static bool Disponivel
        {
            get
            {
                return Application.platform == RuntimePlatform.WindowsEditor ||
                       Application.platform == RuntimePlatform.WindowsPlayer;
            }
        }

        public static bool Suportado(string caminho)
        {
            string extensao = Extensao(caminho);
            return extensao == ".pdf" || extensao == ".pptx" || extensao == ".ppt";
        }

        /// <summary>
        /// Pasta de imagens desta apresentação, dentro dos dados do app. Fica fora da pasta
        /// do usuário de propósito: exportação é subproduto, não arquivo dele. A chave
        /// carrega tamanho e data do arquivo, então editar os slides e apresentar de novo
        /// gera uma pasta nova em vez de projetar a versão velha.
        /// </summary>
        public static string PastaDeCache(string apresentacao)
        {
            FileInfo info = new FileInfo(apresentacao);
            if (!info.Exists)
            {
                return null;
            }

            string chave = apresentacao.ToLowerInvariant() + "|" + info.Length + "|" +
                           info.LastWriteTimeUtc.Ticks;

            return Path.Combine(Path.Combine(Application.persistentDataPath, "slides"),
                                Path.GetFileNameWithoutExtension(apresentacao) + "-" + Impressao(chave));
        }

        /// <summary>Converte para <paramref name="destino"/>. Devolve null se deu certo.</summary>
        public static string Converter(string apresentacao, string destino)
        {
            if (!File.Exists(apresentacao))
            {
                return "Arquivo não encontrado: " + apresentacao;
            }

            try
            {
                Directory.CreateDirectory(destino);
                foreach (string antigo in Directory.GetFiles(destino))
                {
                    File.Delete(antigo);
                }

                return Extensao(apresentacao) == ".pdf"
                    ? ConverterPdf(apresentacao, destino)
                    : ConverterPowerPoint(apresentacao, destino);
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        public static int ContarImagens(string pasta)
        {
            if (!Directory.Exists(pasta))
            {
                return 0;
            }

            int total = 0;
            foreach (string arquivo in Directory.GetFiles(pasta))
            {
                if (Extensao(arquivo) == ".png")
                {
                    total++;
                }
            }

            return total;
        }

        /// <summary>
        /// PPTx pelo PowerPoint da máquina, via COM. A altura sai da proporção do slide:
        /// forçar 1920x1080 num deck 4:3 entrega letra achatada.
        /// </summary>
        private static string ConverterPowerPoint(string apresentacao, string destino)
        {
            string script = Path.Combine(Path.GetTempPath(), "podium_exportar_slides.ps1");
            StringBuilder ps = new StringBuilder();

            ps.AppendLine("$ErrorActionPreference = 'Stop'");
            ps.AppendLine("$origem = " + Literal(apresentacao));
            ps.AppendLine("$destino = " + Literal(destino));
            ps.AppendLine("$ppt = New-Object -ComObject PowerPoint.Application");
            ps.AppendLine("try {");
            // Sem janela: abrir visível roubaria o foco de quem está apresentando.
            ps.AppendLine("  $apresentacao = $ppt.Presentations.Open($origem, $true, $false, $false)");
            ps.AppendLine("  try {");
            ps.AppendLine("    $largura = " + LarguraDaExportacao);
            ps.AppendLine("    $altura = [int][math]::Round($largura * " +
                          "$apresentacao.PageSetup.SlideHeight / $apresentacao.PageSetup.SlideWidth)");
            ps.AppendLine("    $apresentacao.Export($destino, 'PNG', $largura, $altura)");
            ps.AppendLine("  } finally { $apresentacao.Close() }");
            ps.AppendLine("} finally { $ppt.Quit() }");

            File.WriteAllText(script, ps.ToString(), new UTF8Encoding(true));

            string erro = Rodar("powershell",
                                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" +
                                script + "\"");

            if (erro != null)
            {
                return erro;
            }

            return ContarImagens(destino) == 0
                ? "O PowerPoint não deixou nenhuma imagem na pasta."
                : null;
        }

        /// <summary>
        /// PDF pelo PyMuPDF do container. O arquivo entra e as imagens saem por docker cp,
        /// então funciona para PDF em qualquer lugar do disco, e não só no volume montado.
        /// </summary>
        private static string ConverterPdf(string apresentacao, string destino)
        {
            string script = Path.Combine(Path.GetTempPath(), "podium_exportar_slides.py");
            File.WriteAllText(script, ScriptPython(), new UTF8Encoding(false));

            string erro = Rodar("docker",
                                "cp \"" + apresentacao + "\" " + ContainerDaApi +
                                ":/tmp/podium_slides.pdf");
            if (erro != null)
            {
                return erro + "\n\nPara PDF o backend precisa estar no ar: " +
                       "docker compose up -d na pasta podium.";
            }

            erro = Rodar("docker",
                         "cp \"" + script + "\" " + ContainerDaApi +
                         ":/tmp/podium_exportar_slides.py");
            if (erro != null)
            {
                return erro;
            }

            erro = Rodar("docker", "exec " + ContainerDaApi + " python /tmp/podium_exportar_slides.py");
            if (erro != null)
            {
                return erro;
            }

            // A barra com ponto no fim copia o conteúdo da pasta, e não a pasta dentro dela.
            erro = Rodar("docker",
                         "cp " + ContainerDaApi + ":/tmp/podium_slides_saida/. \"" + destino + "\"");
            if (erro != null)
            {
                return erro;
            }

            return ContarImagens(destino) == 0 ? "O container não devolveu nenhuma imagem." : null;
        }

        private static string ScriptPython()
        {
            StringBuilder py = new StringBuilder();
            py.AppendLine("import pathlib");
            py.AppendLine("import fitz");
            py.AppendLine("");
            py.AppendLine("saida = pathlib.Path('/tmp/podium_slides_saida')");
            py.AppendLine("saida.mkdir(exist_ok=True)");
            py.AppendLine("for antigo in saida.glob('*.png'):");
            py.AppendLine("    antigo.unlink()");
            py.AppendLine("");
            py.AppendLine("documento = fitz.open('/tmp/podium_slides.pdf')");
            py.AppendLine("for numero, pagina in enumerate(documento, start=1):");
            py.AppendLine("    pagina.get_pixmap(dpi=150).save(saida / ('slide%03d.png' % numero))");
            py.AppendLine("");
            py.AppendLine("print(len(documento))");
            return py.ToString();
        }

        /// <summary>Roda o processo e devolve null em caso de sucesso, ou o que ele reclamou.</summary>
        private static string Rodar(string programa, string argumentos)
        {
            ProcessStartInfo info = new ProcessStartInfo(programa, argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (Process processo = Process.Start(info))
            {
                if (processo == null)
                {
                    return "Não deu para iniciar " + programa + ".";
                }

                string saida = processo.StandardOutput.ReadToEnd();
                string reclamacao = processo.StandardError.ReadToEnd();

                if (!processo.WaitForExit(TempoLimiteMs))
                {
                    processo.Kill();
                    return programa + " passou de " + (TempoLimiteMs / 1000) + " s e foi encerrado.";
                }

                if (processo.ExitCode == 0)
                {
                    return null;
                }

                string detalhe = string.IsNullOrEmpty(reclamacao) ? saida : reclamacao;
                return programa + " terminou com código " + processo.ExitCode + ":\n" + detalhe.Trim();
            }
        }

        /// <summary>Literal de string do PowerShell: aspas simples, com a aspa interna dobrada.</summary>
        private static string Literal(string valor)
        {
            return "'" + valor.Replace("'", "''") + "'";
        }

        private static string Extensao(string caminho)
        {
            return Path.GetExtension(caminho).ToLowerInvariant();
        }

        /// <summary>
        /// FNV-1a, e não string.GetHashCode(): o hash do .NET muda a cada execução do
        /// processo, e a pasta de cache precisa ser a mesma amanhã para o mesmo arquivo.
        /// </summary>
        private static string Impressao(string texto)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < texto.Length; i++)
                {
                    hash ^= texto[i];
                    hash *= 16777619;
                }

                return hash.ToString("x8");
            }
        }
    }
}
