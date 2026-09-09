using System.IO;
using Podium.Slides;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Podium.EditorTools
{
    /// <summary>
    /// Exporta a apresentação em PNG numa pasta ao lado do arquivo.
    /// <para>
    /// A sala não depende disto: apontar o PDF ou o PPTx no lobby basta, porque a
    /// <see cref="ApresentacaoDeSlides"/> converte sozinha para um cache interno. Este menu
    /// existe para quando você quer as imagens à mão — conferir o que a banca vai ver,
    /// levar os PNG para outra máquina, ou usar um deck que a conversão automática não dá
    /// conta (um PPTx numa máquina sem Office, exportado antes em outro lugar).
    /// </para>
    /// </summary>
    public static class PodiumSlideExporter
    {
        private const string ChaveDoUltimoArquivo = "Podium.UltimaApresentacaoExportada";

        [MenuItem("Podium/Exportar slides da apresentação")]
        public static void Exportar()
        {
            string ultimo = EditorPrefs.GetString(ChaveDoUltimoArquivo, string.Empty);
            string pastaInicial = string.IsNullOrEmpty(ultimo)
                ? string.Empty
                : (Path.GetDirectoryName(ultimo) ?? string.Empty);

            string arquivo = EditorUtility.OpenFilePanelWithFilters(
                "Apresentação a exportar", pastaInicial,
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

            string erro;
            try
            {
                EditorUtility.DisplayProgressBar("Podium", "Exportando os slides...", 0.5f);
                erro = ConversorDeSlides.Converter(arquivo, destino);
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

            int imagens = ConversorDeSlides.ContarImagens(destino);
            Debug.Log("[Podium] " + imagens + " slides exportados em " + destino);
            EditorUtility.DisplayDialog(
                "Podium",
                imagens + " slides exportados em:\n" + destino +
                "\n\nA cena usa esta pasta quando ela existe, em vez de converter de novo.",
                "Certo");
        }

        /// <summary>
        /// A pasta irmã que a <c>ApresentacaoDeSlides</c> procura antes de converter:
        /// tcc_slides.pdf vira tcc_slides_slides/.
        /// </summary>
        private static string PastaDeSaida(string arquivo)
        {
            string pasta = Path.GetDirectoryName(arquivo) ?? string.Empty;
            return Path.Combine(pasta,
                                Path.GetFileNameWithoutExtension(arquivo) +
                                ConversorDeSlides.SufixoDaPasta);
        }

        /// <summary>
        /// Apaga a exportação anterior, com confirmação. Sobrescrever sem limpar deixa os
        /// slides do deck antigo no fim da fila quando a apresentação encurta — e uma
        /// pergunta ancorada no slide 12 passa a apontar para material que saiu.
        /// </summary>
        private static bool PrepararDestino(string destino)
        {
            if (!Directory.Exists(destino))
            {
                return true;
            }

            string[] antigos = Directory.GetFiles(destino);
            if (antigos.Length == 0)
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Podium",
                "A pasta já existe com " + antigos.Length + " arquivo(s):\n" + destino +
                "\n\nApagar e exportar de novo?",
                "Apagar e exportar", "Cancelar");
        }
    }
}
