using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Podium.EditorTools
{
    /// <summary>
    /// Baixa o modelo de transcrição para dentro de <c>StreamingAssets</c>.
    /// <para>
    /// O modelo não entra no repositório: são dezenas de megabytes de binário que o Git
    /// guardaria inteiro a cada troca, num projeto cujo código todo tem alguns milhares de
    /// linhas. Fica no <c>.gitignore</c> e se baixa uma vez por máquina — o mesmo trato
    /// que o projeto já faz com a pasta <c>Library</c>.
    /// </para>
    /// </summary>
    public static class PodiumModeloSetup
    {
        /// <summary>Caminho relativo a StreamingAssets, que é como o WhisperManager o lê.</summary>
        public const string CaminhoRelativo = "Whisper/ggml-base-q5_1.bin";

        private const string Origem =
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin";

        // Conferido no servidor em 2026-09-19. Serve para detectar download truncado:
        // um .bin pela metade não dá erro ao carregar, dá transcrição em branco.
        private const long TamanhoEsperado = 59707625;

        public static string CaminhoAbsoluto
        {
            get { return Path.Combine(Application.streamingAssetsPath, CaminhoRelativo); }
        }

        [MenuItem("Podium/Baixar o modelo de transcrição")]
        public static void Baixar()
        {
            string destino = CaminhoAbsoluto;

            if (File.Exists(destino) && new FileInfo(destino).Length == TamanhoEsperado)
            {
                Debug.Log("[Podium] O modelo já está em " + destino + ".");
                return;
            }

            // Em modo de lote não há ninguém para responder, e uma caixa de diálogo ali
            // trava a execução até o tempo limite.
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "Podium",
                    "Baixar o modelo de transcrição (whisper base, ~57 MB) do Hugging Face?\n\n" +
                    "Ele fica em StreamingAssets e não vai para o repositório.",
                    "Baixar", "Cancelar"))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destino));

            try
            {
                EditorUtility.DisplayProgressBar("Podium", "Baixando o modelo...", 0.1f);
                BaixarArquivo(Origem, destino).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                File.Delete(destino);
                Debug.LogError("[Podium] O download falhou: " + e.Message);
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            long tamanho = new FileInfo(destino).Length;
            if (tamanho != TamanhoEsperado)
            {
                File.Delete(destino);
                Debug.LogError("[Podium] O arquivo veio com " + tamanho + " bytes em vez de " +
                               TamanhoEsperado + "; apaguei. Um modelo truncado não dá erro " +
                               "ao carregar, dá transcrição em branco.");
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log("[Podium] Modelo salvo em " + destino + ".");
        }

        private static async Task BaixarArquivo(string url, string destino)
        {
            using (HttpClient cliente = new HttpClient())
            {
                cliente.Timeout = TimeSpan.FromMinutes(15);

                using (var resposta = await cliente.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    resposta.EnsureSuccessStatusCode();

                    using (var entrada = await resposta.Content.ReadAsStreamAsync())
                    using (var saida = File.Create(destino))
                    {
                        await entrada.CopyToAsync(saida);
                    }
                }
            }
        }
    }
}
