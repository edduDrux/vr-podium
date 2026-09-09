using UnityEngine;

namespace Podium.Slides
{
    /// <summary>
    /// Superfície que mostra o slide no ar. A tela da parede e o monitor de retorno do
    /// púlpito são duas instâncias disto ligadas à mesma <see cref="ApresentacaoDeSlides"/>:
    /// quem apresenta lê o slide sem virar de costas para a banca.
    /// <para>
    /// A superfície se redimensiona para a proporção da imagem em vez de esticá-la. Slide
    /// 4:3 esticado numa moldura 16:9 deforma a letra, e material ilegível na parede é o
    /// mesmo que parede vazia.
    /// </para>
    /// </summary>
    public class TelaDeSlides : MonoBehaviour
    {
        [Tooltip("Maior largura, em metros, que o slide pode ocupar.")]
        [SerializeField] private float larguraMaxima = 3.2f;

        [Tooltip("Maior altura, em metros, que o slide pode ocupar.")]
        [SerializeField] private float alturaMaxima = 1.8f;

        private static readonly Color CorVazia = new Color(0.11f, 0.12f, 0.14f);

        private ApresentacaoDeSlides apresentacao;
        private Material material;

        /// <summary>
        /// Liga a superfície à apresentação. Chamado pela cenografia, que cria a tela em
        /// tempo de execução; o material é próprio para não pintar de branco todo primitivo
        /// que compartilhe a cor.
        /// </summary>
        public void Conectar(ApresentacaoDeSlides fonte, float largura, float altura)
        {
            larguraMaxima = largura;
            alturaMaxima = altura;

            Renderer superficie = GetComponent<Renderer>();
            if (superficie != null && material == null)
            {
                material = new Material(superficie.sharedMaterial.shader);

                // Sem face oculta: o Quad tem uma face só, e uma tela que some porque foi
                // montada de costas é um defeito silencioso — a moldura continua lá.
                if (material.HasProperty("_Cull"))
                {
                    material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                }

                superficie.sharedMaterial = material;
            }

            if (apresentacao != null)
            {
                apresentacao.SlideExibido -= Mostrar;
            }

            apresentacao = fonte;

            if (apresentacao != null)
            {
                apresentacao.SlideExibido += Mostrar;
            }

            Mostrar(null);
        }

        private void OnDestroy()
        {
            if (apresentacao != null)
            {
                apresentacao.SlideExibido -= Mostrar;
            }

            if (material != null)
            {
                // DestroyImmediate fora do Play: a pré-visualização do Editor monta e
                // derruba a sala sem nunca entrar em modo de execução.
                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }
        }

        private void Mostrar(Texture2D slide)
        {
            if (material == null)
            {
                return;
            }

            material.mainTexture = slide;
            material.color = slide != null ? Color.white : CorVazia;

            transform.localScale = slide != null
                ? EscalaPara(slide.width, slide.height)
                : new Vector3(larguraMaxima, alturaMaxima, 1f);
        }

        /// <summary>Encaixa a imagem na moldura sem deformá-la: cabe pela largura ou pela altura.</summary>
        private Vector3 EscalaPara(int larguraDaImagem, int alturaDaImagem)
        {
            if (larguraDaImagem <= 0 || alturaDaImagem <= 0)
            {
                return new Vector3(larguraMaxima, alturaMaxima, 1f);
            }

            float proporcao = larguraDaImagem / (float)alturaDaImagem;
            float largura = larguraMaxima;
            float altura = largura / proporcao;

            if (altura > alturaMaxima)
            {
                altura = alturaMaxima;
                largura = altura * proporcao;
            }

            return new Vector3(largura, altura, 1f);
        }
    }
}
