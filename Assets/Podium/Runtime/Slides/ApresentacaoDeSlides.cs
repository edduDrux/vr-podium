using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Podium.Api;
using UnityEngine;
using UnityEngine.Networking;

namespace Podium.Slides
{
    /// <summary>
    /// A apresentação dentro da sala: encontra as imagens dos slides, guarda qual está no
    /// ar e atende aos comandos de passar e voltar.
    /// <para>
    /// Recebe o mesmo caminho que sobe no /init — o PDF ou o PPTx — e resolve o resto
    /// sozinha, porque o runtime do Unity não rasteriza nenhum dos dois formatos e ninguém
    /// deveria precisar saber disso para ensaiar uma defesa. A ordem é: pasta de imagens, se
    /// o caminho apontar para uma; a pasta irmã <c>&lt;arquivo&gt;_slides/</c>, se existir; e
    /// senão o <see cref="ConversorDeSlides"/> converte o arquivo, uma vez por versão dele.
    /// A banca analisa o arquivo; a sala projeta a conversão dele.
    /// </para>
    /// </summary>
    public class ApresentacaoDeSlides : MonoBehaviour
    {
        [Header("Origem das imagens")]
        [Tooltip("Sufixo da pasta irmã do arquivo: tcc_slides.pdf procura tcc_slides_slides/. " +
                 "Se o caminho da apresentação já for uma pasta, ela é usada direto.")]
        [SerializeField] private string sufixoDaPasta = "_slides";

        [Header("Comandos")]
        [SerializeField] private bool atalhosDeTeclado = true;

        [SerializeField] private KeyCode[] teclasProximo =
        {
            KeyCode.RightArrow, KeyCode.PageDown, KeyCode.Space,
        };

        [SerializeField] private KeyCode[] teclasAnterior =
        {
            KeyCode.LeftArrow, KeyCode.PageUp,
        };

        private static readonly string[] ExtensoesAceitas = { ".png", ".jpg", ".jpeg" };

        private readonly List<string> arquivos = new List<string>();

        // Só o slide atual e os dois vizinhos ficam em memória: um deck de 40 páginas em
        // 1920x1080 passaria de 300 MB de textura se fosse carregado inteiro, e um standalone
        // não tem esse fôlego. O custo é uma corrotina por virada, não um congelamento.
        private readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();
        private readonly HashSet<int> carregando = new HashSet<int>();
        private readonly List<int> descartaveis = new List<int>();

        private int indiceAtual = -1;
        private string diagnostico = "Nenhuma apresentação carregada.";

        // Origem remota: no headset não há PowerPoint nem Docker para converter, mas o
        // backend já tem o arquivo desde o /init e sabe desenhar as páginas.
        private PodiumApiClient apiDosSlides;
        private string sessaoDosSlides;
        private int larguraRemota;
        private int totalRemoto;

        // Escritos pela thread da conversão, lidos pela corrotina que a espera.
        private volatile bool conversaoTerminou;
        private volatile string erroDaConversao;

        /// <summary>Dispara a cada troca de slide. Null significa "nada para mostrar".</summary>
        public event Action<Texture2D> SlideExibido;

        public int Total
        {
            get { return totalRemoto > 0 ? totalRemoto : arquivos.Count; }
        }

        /// <summary>As imagens vêm do backend, e não de uma pasta local.</summary>
        public bool VemDoServidor
        {
            get { return totalRemoto > 0; }
        }

        /// <summary>Base zero. Vale -1 enquanto nenhum slide foi exibido.</summary>
        public int IndiceAtual
        {
            get { return indiceAtual; }
        }

        public bool TemSlides
        {
            get { return Total > 0; }
        }

        /// <summary>Frase pronta para a interface: quantos slides, de onde, ou o que faltou.</summary>
        public string Diagnostico
        {
            get { return diagnostico; }
        }

        /// <summary>
        /// Desligável porque as mesmas setas navegam as perguntas da banca depois da
        /// análise: duas coisas andando com a mesma tecla é surpresa, não atalho.
        /// </summary>
        public bool AtalhosDeTeclado
        {
            get { return atalhosDeTeclado; }
            set { atalhosDeTeclado = value; }
        }

        /// <summary>
        /// Aponta a sala para as imagens da apresentação. Recebe o mesmo caminho que vai no
        /// /init, e não a pasta, para que a tela e a banca nunca fiquem em arquivos
        /// diferentes por descuido de configuração.
        /// </summary>
        public void Carregar(string caminhoDaApresentacao)
        {
            Limpar();

            string pasta;
            try
            {
                pasta = ResolverPasta(caminhoDaApresentacao);
            }
            catch (ArgumentException e)
            {
                diagnostico = "Caminho inválido: " + e.Message;
                Exibir(null);
                return;
            }

            if (pasta != null)
            {
                UsarPasta(pasta, "em " + pasta);
                return;
            }

            // Ninguém precisa preparar pasta nenhuma: o PDF ou o PPTx apontado no lobby é
            // convertido aqui mesmo, uma vez por versão do arquivo.
            if (ConversorDeSlides.Disponivel && ConversorDeSlides.Suportado(caminhoDaApresentacao))
            {
                Converter(caminhoDaApresentacao);
                return;
            }

            diagnostico = "Sem imagens para \"" + Path.GetFileName(caminhoDaApresentacao) +
                          "\": nesta plataforma a conversão não roda; exporte os slides como " +
                          "PNG na pasta \"" + PastaEsperada(caminhoDaApresentacao) + "\".";
            Exibir(null);
        }

        /// <summary>
        /// Projeta as páginas que o próprio backend desenha, em vez de imagens em disco.
        /// <para>
        /// É o caminho do headset: lá não há PowerPoint nem Docker para converter, e
        /// embutir um rasterizador de PDF no app seria dependência nativa só para pintar
        /// uma parede. O arquivo já está no servidor desde o /init, e quem desenha é o
        /// mesmo PyMuPDF que extraiu o texto — a página 7 da parede é a página 7 da âncora.
        /// </para>
        /// </summary>
        public IEnumerator CarregarDoServidor(PodiumApiClient api, string sessionId)
        {
            Limpar();

            if (api == null || string.IsNullOrEmpty(sessionId))
            {
                diagnostico = "Sem sessão aberta: não dá para pedir os slides ao servidor.";
                Exibir(null);
                yield break;
            }

            diagnostico = "Pedindo os slides ao servidor...";
            Exibir(null);

            PodiumResult<SlidesInfoResponse> r = null;
            yield return api.GetSlidesInfo(sessionId, x => r = x);

            if (!r.Ok)
            {
                diagnostico = "O servidor não informou os slides: " + r.Message;
                Exibir(null);
                yield break;
            }

            if (!r.Value.renderizavel || r.Value.total <= 0)
            {
                diagnostico = string.IsNullOrEmpty(r.Value.motivo)
                    ? "O servidor não tem imagens desta apresentação."
                    : r.Value.motivo;
                Exibir(null);
                yield break;
            }

            apiDosSlides = api;
            sessaoDosSlides = sessionId;
            totalRemoto = r.Value.total;
            larguraRemota = r.Value.largura_padrao > 0 ? r.Value.largura_padrao : 1600;

            diagnostico = totalRemoto + " slides servidos pelo backend";
            Ir(0);
        }

        private void UsarPasta(string pasta, string origem)
        {
            arquivos.Clear();

            foreach (string arquivo in Directory.GetFiles(pasta))
            {
                if (Aceita(arquivo))
                {
                    arquivos.Add(arquivo);
                }
            }

            if (arquivos.Count == 0)
            {
                diagnostico = "A pasta " + pasta + " não tem PNG nem JPG.";
                Exibir(null);
                return;
            }

            arquivos.Sort(CompararNomes);
            diagnostico = arquivos.Count + " slides " + origem;
            Ir(0);
        }

        /// <summary>
        /// Converte a apresentação em imagens. A pasta é reaproveitada entre sessões: a
        /// chave dela carrega tamanho e data do arquivo, então só reconverte o que mudou.
        /// </summary>
        private void Converter(string caminho)
        {
            string destino = ConversorDeSlides.PastaDeCache(caminho);
            if (destino == null)
            {
                diagnostico = "Arquivo não encontrado: " + caminho;
                Exibir(null);
                return;
            }

            if (ConversorDeSlides.ContarImagens(destino) > 0)
            {
                UsarPasta(destino, "do " + Path.GetExtension(caminho).TrimStart('.').ToUpperInvariant());
                return;
            }

            // Fora do Play (pré-visualização do Editor) não há corrotina: converte na hora.
            if (!Application.isPlaying)
            {
                string falha = ConversorDeSlides.Converter(caminho, destino);
                if (falha != null)
                {
                    diagnostico = "Não deu para converter: " + falha;
                    Exibir(null);
                    return;
                }

                UsarPasta(destino, "do " + Path.GetExtension(caminho).TrimStart('.').ToUpperInvariant());
                return;
            }

            StartCoroutine(ConverterEUsar(caminho, destino));
        }

        /// <summary>
        /// A conversão chama PowerPoint ou Docker e leva segundos. Numa thread à parte, o
        /// painel continua desenhando e dizendo o que está acontecendo — travar a tela seria
        /// indistinguível de travar de vez.
        /// </summary>
        private IEnumerator ConverterEUsar(string caminho, string destino)
        {
            diagnostico = "Convertendo " + Path.GetFileName(caminho) +
                          " em imagens (só na primeira vez)...";
            Exibir(null);

            conversaoTerminou = false;
            erroDaConversao = null;

            Thread trabalho = new Thread(() =>
            {
                erroDaConversao = ConversorDeSlides.Converter(caminho, destino);
                conversaoTerminou = true;
            });

            trabalho.IsBackground = true;
            trabalho.Start();

            while (!conversaoTerminou)
            {
                yield return null;
            }

            if (erroDaConversao != null)
            {
                diagnostico = "Não deu para converter " + Path.GetFileName(caminho) + ": " +
                              erroDaConversao;
                Exibir(null);
                yield break;
            }

            UsarPasta(destino, "do " + Path.GetExtension(caminho).TrimStart('.').ToUpperInvariant());
        }

        /// <summary>Esvazia a sala: apaga as texturas e devolve a tela ao estado vazio.</summary>
        public void Limpar()
        {
            StopAllCoroutines();
            carregando.Clear();

            foreach (KeyValuePair<int, Texture2D> par in cache)
            {
                Apagar(par.Value);
            }

            cache.Clear();
            arquivos.Clear();
            indiceAtual = -1;

            apiDosSlides = null;
            sessaoDosSlides = null;
            totalRemoto = 0;
        }

        public void Proximo()
        {
            Ir(indiceAtual + 1);
        }

        public void Anterior()
        {
            Ir(indiceAtual - 1);
        }

        /// <summary>Vai para um slide (base zero). Fora da faixa, para na borda.</summary>
        public void Ir(int indice)
        {
            if (Total == 0)
            {
                return;
            }

            indice = Mathf.Clamp(indice, 0, Total - 1);
            if (indice == indiceAtual && cache.ContainsKey(indice))
            {
                return;
            }

            indiceAtual = indice;

            Texture2D pronta;
            if (cache.TryGetValue(indice, out pronta))
            {
                Exibir(pronta);

                if (Application.isPlaying)
                {
                    PrepararVizinhos();
                }

                return;
            }

            // Fora do Play não existe corrotina: a pré-visualização do Editor lê o arquivo
            // de uma vez. É um slide só, e ninguém está apresentando.
            if (!Application.isPlaying)
            {
                if (CarregarDeUmaVez(indice))
                {
                    Exibir(cache[indice]);
                }

                return;
            }

            StartCoroutine(TrocarPara(indice));
        }

        /// <summary>
        /// Ordem natural dos nomes: slide2 vem antes de slide10. Ordenar por texto puro
        /// inverteria os dois, e a apresentação sairia fora de ordem no meio da fala.
        /// </summary>
        public static int CompararNomes(string a, string b)
        {
            string na = Path.GetFileName(a);
            string nb = Path.GetFileName(b);

            int i = 0;
            int j = 0;

            while (i < na.Length && j < nb.Length)
            {
                if (char.IsDigit(na[i]) && char.IsDigit(nb[j]))
                {
                    int inicioA = i;
                    int inicioB = j;

                    while (i < na.Length && char.IsDigit(na[i]))
                    {
                        i++;
                    }

                    while (j < nb.Length && char.IsDigit(nb[j]))
                    {
                        j++;
                    }

                    // Comparação por dígitos, e não convertendo para int: exportador que
                    // carimba data no nome gera número maior do que um int aguenta.
                    string numeroA = na.Substring(inicioA, i - inicioA).TrimStart('0');
                    string numeroB = nb.Substring(inicioB, j - inicioB).TrimStart('0');

                    if (numeroA.Length != numeroB.Length)
                    {
                        return numeroA.Length - numeroB.Length;
                    }

                    int comparacaoNumerica = string.CompareOrdinal(numeroA, numeroB);
                    if (comparacaoNumerica != 0)
                    {
                        return comparacaoNumerica;
                    }

                    continue;
                }

                int comparacao = char.ToLowerInvariant(na[i]).CompareTo(char.ToLowerInvariant(nb[j]));
                if (comparacao != 0)
                {
                    return comparacao;
                }

                i++;
                j++;
            }

            return (na.Length - i) - (nb.Length - j);
        }

        private void Update()
        {
            if (!atalhosDeTeclado || Total == 0)
            {
                return;
            }

            if (AlgumaTeclaDesceu(teclasProximo))
            {
                Proximo();
            }
            else if (AlgumaTeclaDesceu(teclasAnterior))
            {
                Anterior();
            }
        }

        private void OnDestroy()
        {
            Limpar();
        }

        private static bool AlgumaTeclaDesceu(KeyCode[] teclas)
        {
            if (teclas == null)
            {
                return false;
            }

            for (int i = 0; i < teclas.Length; i++)
            {
                if (Input.GetKeyDown(teclas[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator TrocarPara(int indice)
        {
            yield return CarregarArquivo(indice);

            // Quem apertou "próximo" três vezes seguidas já passou deste slide: exibir
            // agora seria voltar no tempo.
            if (indice != indiceAtual)
            {
                yield break;
            }

            Texture2D slide;
            if (cache.TryGetValue(indice, out slide))
            {
                Exibir(slide);
            }

            PrepararVizinhos();
        }

        private IEnumerator CarregarArquivo(int indice)
        {
            if (indice < 0 || indice >= Total ||
                cache.ContainsKey(indice) || carregando.Contains(indice))
            {
                yield break;
            }

            carregando.Add(indice);

            if (VemDoServidor)
            {
                PodiumResult<Texture2D> resposta = null;
                yield return apiDosSlides.GetSlideTexture(
                    sessaoDosSlides, indice + 1, larguraRemota, x => resposta = x);

                carregando.Remove(indice);

                if (resposta.Ok && resposta.Value != null)
                {
                    cache[indice] = resposta.Value;
                }
                else
                {
                    Debug.LogWarning("[Podium] Slide " + (indice + 1) + " não veio do " +
                                     "servidor: " + resposta.Message);
                }

                yield break;
            }

            // UnityWebRequest, e não File.ReadAllBytes com LoadImage: decodifica fora da
            // thread principal e devolve a textura não-legível, que ocupa metade da memória.
            string uri = new Uri(arquivos[indice]).AbsoluteUri;
            using (UnityWebRequest requisicao = UnityWebRequestTexture.GetTexture(uri, true))
            {
                yield return requisicao.SendWebRequest();
                carregando.Remove(indice);

                if (requisicao.result != UnityWebRequest.Result.Success)
                {
                    // Nome de arquivo de apresentação costuma ter espaço, parêntese e
                    // acento, que viram %20 e %C3%B3 na URI. Se o file:// tropeçar em
                    // algum deles, ler o arquivo direto custa uma pausa curta, uma vez
                    // por slide — melhor que parede vazia sem explicação.
                    if (CarregarDeUmaVez(indice))
                    {
                        yield break;
                    }

                    Debug.LogWarning("[Podium] Slide " + (indice + 1) + " não carregou: " +
                                     requisicao.error);
                    yield break;
                }

                cache[indice] = DownloadHandlerTexture.GetContent(requisicao);
            }
        }

        private bool CarregarDeUmaVez(int indice)
        {
            try
            {
                Texture2D textura = new Texture2D(2, 2);
                if (textura.LoadImage(File.ReadAllBytes(arquivos[indice])))
                {
                    cache[indice] = textura;
                    return true;
                }

                Apagar(textura);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Podium] Slide " + (indice + 1) + " não carregou: " + e.Message);
            }

            return false;
        }

        private static void Apagar(UnityEngine.Object alvo)
        {
            if (alvo == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(alvo);
            }
            else
            {
                DestroyImmediate(alvo);
            }
        }

        private void PrepararVizinhos()
        {
            DescartarDistantes();

            if (indiceAtual + 1 < Total)
            {
                StartCoroutine(CarregarArquivo(indiceAtual + 1));
            }

            if (indiceAtual - 1 >= 0)
            {
                StartCoroutine(CarregarArquivo(indiceAtual - 1));
            }
        }

        private void DescartarDistantes()
        {
            descartaveis.Clear();

            foreach (KeyValuePair<int, Texture2D> par in cache)
            {
                if (Mathf.Abs(par.Key - indiceAtual) > 1)
                {
                    descartaveis.Add(par.Key);
                }
            }

            for (int i = 0; i < descartaveis.Count; i++)
            {
                int indice = descartaveis[i];
                Destroy(cache[indice]);
                cache.Remove(indice);
            }
        }

        private void Exibir(Texture2D slide)
        {
            Action<Texture2D> ouvintes = SlideExibido;
            if (ouvintes != null)
            {
                ouvintes(slide);
            }
        }

        private string ResolverPasta(string caminho)
        {
            if (string.IsNullOrEmpty(caminho))
            {
                return null;
            }

            caminho = caminho.Trim();
            if (caminho.Length == 0)
            {
                return null;
            }

            if (Directory.Exists(caminho))
            {
                return caminho;
            }

            string pasta = Path.GetDirectoryName(caminho);
            if (string.IsNullOrEmpty(pasta))
            {
                return null;
            }

            string nome = Path.GetFileNameWithoutExtension(caminho);
            string comSufixo = Path.Combine(pasta, nome + sufixoDaPasta);
            if (Directory.Exists(comSufixo))
            {
                return comSufixo;
            }

            string mesmoNome = Path.Combine(pasta, nome);
            return Directory.Exists(mesmoNome) ? mesmoNome : null;
        }

        private string PastaEsperada(string caminho)
        {
            if (string.IsNullOrEmpty(caminho))
            {
                return "<arquivo>" + sufixoDaPasta;
            }

            return Path.GetFileNameWithoutExtension(caminho) + sufixoDaPasta;
        }

        private static bool Aceita(string arquivo)
        {
            string extensao = Path.GetExtension(arquivo).ToLowerInvariant();
            for (int i = 0; i < ExtensoesAceitas.Length; i++)
            {
                if (extensao == ExtensoesAceitas[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
