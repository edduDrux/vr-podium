using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Podium.EditorTools
{
    /// <summary>
    /// Gera a pasta de imagens que a Sala de Aula projeta, a partir do PDF ou do PPTx que
    /// vai subir no /init.
    /// <para>
    /// O runtime do Unity não rasteriza nenhum dos dois formatos, então alguém precisa
    /// transformar as páginas em PNG. Exportar à mão é um passo que se esquece — e o
    /// esquecimento só aparece com a sala montada e a banca sentada. Aqui a máquina do
    /// desenvolvedor faz o trabalho: PPTx pelo PowerPoint instalado, PDF pelo PyMuPDF que
    /// já existe no container do backend. Nenhuma dependência nova entra no projeto Unity.
    /// </para>
    /// </summary>
    public static class PodiumSlideExporter
    {
        private const string ChaveDoUltimoArquivo = "Podium.UltimaApresentacaoExportada";
        private const string ContainerDaApi = "podium_api";
        private const string SufixoDaPasta = "_slides";
        private const int LarguraDaExportacao = 1920;
        private const int TempoLimiteMs = 180000;

        [MenuItem("Podium/Exportar slides da apresentação")]
        public static void Exportar()
        {
            string ultimo = EditorPrefs.GetString(ChaveDoUltimoArquivo, string.Empty);
            string pastaInicial = string.IsNullOrEmpty(ultimo) ? string.Empty : DiretorioDe(ultimo);

            string arquivo = EditorUtility.OpenFilePanelWithFilters(
                "Apresentação a projetar na sala", pastaInicial,
                new[] { "Apresentação (PDF ou PPTx)", "pdf,pptx,ppt" });

            if (string.IsNullOrEmpty(arquivo))
            {
                return;
            }

            EditorPrefs.SetString(ChaveDoUltimoArquivo, arquivo);

            string destino = PastaDeSaida(arquivo);
            if (!PrepararDestino(destino))
            {
                return;
            }

            string extensao = Path.GetExtension(arquivo).ToLowerInvariant();
            string erro;

            try
            {
                EditorUtility.DisplayProgressBar("Podium", "Exportando os slides...", 0.5f);
                erro = extensao == ".pdf"
                    ? ExportarPdf(arquivo, destino)
                    : ExportarPowerPoint(arquivo, destino);
            }
            catch (Exception e)
            {
                erro = e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (erro != null)
            {
                Debug.LogError("[Podium] Exportação dos slides falhou: " + erro);
                EditorUtility.DisplayDialog("Podium", "Não deu para exportar os slides.\n\n" + erro,
                                            "Fechar");
                return;
            }

            int imagens = ContarImagens(destino);

            Debug.Log("[Podium] " + imagens + " slides exportados em " + destino);
            EditorUtility.DisplayDialog(
                "Podium",
                imagens + " slides exportados em:\n" + destino +
                "\n\nA cena acha essa pasta sozinha pelo caminho da apresentação.",
                "Certo");
        }

        /// <summary>
        /// A pasta irmã que a <c>ApresentacaoDeSlides</c> procura: tcc_slides.pdf vira
        /// tcc_slides_slides/. A convenção mora nos dois lados de propósito — é ela que
        /// garante que a sala projete o material que a banca leu.
        /// </summary>
        private static string PastaDeSaida(string arquivo)
        {
            return Path.Combine(DiretorioDe(arquivo),
                                Path.GetFileNameWithoutExtension(arquivo) + SufixoDaPasta);
        }

        private static string DiretorioDe(string arquivo)
        {
            string pasta = Path.GetDirectoryName(arquivo);
            return string.IsNullOrEmpty(pasta) ? string.Empty : pasta;
        }

        /// <summary>
        /// Cria a pasta e apaga a exportação anterior. Sobrescrever sem limpar deixa os
        /// slides do deck antigo no fim da fila quando a apresentação encurta — e uma
        /// pergunta ancorada no slide 12 passa a apontar para material que saiu.
        /// </summary>
        private static bool PrepararDestino(string destino)
        {
            if (!Directory.Exists(destino))
            {
                Directory.CreateDirectory(destino);
                return true;
            }

            string[] antigos = Directory.GetFiles(destino);
            if (antigos.Length == 0)
            {
                return true;
            }

            bool apagar = EditorUtility.DisplayDialog(
                "Podium",
                "A pasta já existe com " + antigos.Length + " arquivo(s):\n" + destino +
                "\n\nApagar e exportar de novo?",
                "Apagar e exportar", "Cancelar");

            if (!apagar)
            {
                return false;
            }

            foreach (string antigo in antigos)
            {
                File.Delete(antigo);
            }

            return true;
        }

        /// <summary>
        /// PPTx pelo PowerPoint da própria máquina, via COM. A altura sai da proporção do
        /// slide: forçar 1920x1080 num deck 4:3 entrega letra achatada.
        /// </summary>
        private static string ExportarPowerPoint(string arquivo, string destino)
        {
            string script = Path.Combine(Path.GetTempPath(), "podium_exportar_slides.ps1");
            StringBuilder ps = new StringBuilder();

            ps.AppendLine("$ErrorActionPreference = 'Stop'");
            ps.AppendLine("$origem = " + Literal(arquivo));
            ps.AppendLine("$destino = " + Literal(destino));
            ps.AppendLine("$ppt = New-Object -ComObject PowerPoint.Application");
            ps.AppendLine("try {");
            // ReadOnly, Untitled e WithWindow: abrir sem janela evita roubar o foco do
            // Editor e deixar uma instância visível do PowerPoint para trás.
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

            return erro ?? ConferirSaida(destino,
                "O PowerPoint não deixou nenhuma imagem na pasta.");
        }

        /// <summary>
        /// PDF pelo PyMuPDF do container do backend — a mesma biblioteca que extrai o texto
        /// dos slides, então página numerada aqui é página numerada lá. O arquivo entra e as
        /// imagens saem por docker cp: assim funciona para PDF em qualquer lugar do PC, não
        /// só para os que estão no volume montado.
        /// </summary>
        private static string ExportarPdf(string arquivo, string destino)
        {
            string script = Path.Combine(Path.GetTempPath(), "podium_exportar_slides.py");
            File.WriteAllText(script, ScriptPython(), new UTF8Encoding(false));

            string erro = Rodar("docker",
                                "cp \"" + arquivo + "\" " + ContainerDaApi + ":/tmp/podium_slides.pdf");
            if (erro != null)
            {
                return erro + "\n\nO backend precisa estar no ar: docker compose up -d na pasta podium.";
            }

            erro = Rodar("docker",
                         "cp \"" + script + "\" " + ContainerDaApi + ":/tmp/podium_exportar_slides.py");
            if (erro != null)
            {
                return erro;
            }

            erro = Rodar("docker",
                         "exec " + ContainerDaApi + " python /tmp/podium_exportar_slides.py");
            if (erro != null)
            {
                return erro;
            }

            // A barra e o ponto no fim copiam o conteúdo da pasta, e não a pasta dentro dela.
            erro = Rodar("docker",
                         "cp " + ContainerDaApi + ":/tmp/podium_slides_saida/. \"" + destino + "\"");
            if (erro != null)
            {
                return erro;
            }

            return ConferirSaida(destino, "O container não devolveu nenhuma imagem.");
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

        private static string ConferirSaida(string destino, string mensagem)
        {
            return ContarImagens(destino) == 0 ? mensagem : null;
        }

        /// <summary>
        /// Conta as imagens sem depender do filtro do sistema de arquivos: o PowerPoint
        /// escreve "Slide1.PNG" em maiúsculas, e no Windows o padrão "*.png" casa com ele
        /// enquanto no Linux não casa. Comparar a extensão em C# dá o mesmo número nos dois.
        /// </summary>
        private static int ContarImagens(string destino)
        {
            int total = 0;
            foreach (string arquivo in Directory.GetFiles(destino))
            {
                if (Path.GetExtension(arquivo).ToLowerInvariant() == ".png")
                {
                    total++;
                }
            }

            return total;
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
    }
}
