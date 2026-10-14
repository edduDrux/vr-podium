# podium-vr — Cliente VR do Podium

Frontend em Unity do **Podium**, o simulador de apresentações acadêmicas em VR.
Este projeto grava a apresentação, conversa com o backend (`../podium`) e mostra o
**Feedback Duplo**: perguntas de banca ancoradas no material + métricas de ritmo e pausas.

O backend é a fonte da verdade do contrato. Quando este README e o código do
`podium` divergirem, o `podium` está certo.

- Backend: `C:\Users\eduar\projetos\podium` (FastAPI, Docker)
- Editor: **Unity 6000.5.10f1** (o projeto foi gerado com esta versão)
- Escopo: TCC III 2026/2

## Estado atual

Integração fechada, uma sala jogável no PC e o caminho do headset montado. **Existe:** o
cliente HTTP das oito rotas, gravação de microfone em pedaços, **transcrição da fala no
próprio aparelho enquanto ela acontece**, a máquina de estados da sessão, um teste de
fumaça que roda o fluxo inteiro no Editor, a Sala de Aula com os slides na parede e o
navegador das perguntas da banca, e **OpenXR com o rig, o painel de VR e comandos pelo
controle e pela mão**. **Não existe:** avatares e voz da banca (TTS).

A análise do VR de 2026-09-22 — por que o painel não aparecia no Quest 3 e por que
controle e mão não eram reconhecidos, com as evidências e o que mudou — está em
[`docs/ANALISE-VR.md`](docs/ANALISE-VR.md).

A ordem é deliberada — a integração é o risco técnico do projeto, e ela cabe num teste de
Editor. Modelar sala de aula não é o TCC: a cenografia é de primitivos, e as personas
esperam por `avatarPrefab`.

### Por que transcrever no aparelho

O STT é a etapa mais cara e mais lenta do backend: **11.019 tokens e 124 s** para uma fala
de 7,3 min, medidos numa sessão real (`docs/DECISOES.md` §9 do `podium`). Transcrevendo
aqui, isso acontece **durante** a apresentação, com o whisper.cpp rodando no headset, e o
texto chega pronto ao `/analyze` — que então pula o STT pelo mesmo caminho que já usava
para reaproveitar transcrição. Sobra só a geração das perguntas.

O áudio continua subindo: as métricas de forma (ritmo, pausas) derivam do sinal, não do
texto, e essa independência é um invariante do projeto.

Nada disso é requisito. Sem modelo baixado, sem `WhisperManager` na cena ou com falha no
meio, a sessão segue e o servidor transcreve como antes — só mais devagar. O feedback diz
qual das duas fontes produziu o texto, em `transcript_source`.

## Estrutura

```
Assets/Podium/
  Runtime/Api/        PodiumApiClient · DTOs · PodiumApiConfig · PodiumResult
  Runtime/Audio/      PresentationRecorder (chunks + blocos) · TranscritorLocal · WavEncoder
  Runtime/Session/    PodiumSession (orquestra tudo) · SalaDeAulaController (fluxo + painel de PC)
  Runtime/Scenarios/  ScenarioDefinition · PersonaDefinition (ScriptableObjects) · SalaDeAulaCenografia
  Runtime/Slides/     ApresentacaoDeSlides · TelaDeSlides · ConversorDeSlides (PDF/PPTx > PNG)
  Runtime/XR/         PodiumXRRig (rig do headset) · PainelEmVR (painel no mundo) ·
                      ComandosDoApresentador (botões do controle e gestos da mão)
  Scenarios/          SalaDeAula.asset (único cenário que o backend conhece)
  Personas/           ProfessorRigoroso · OrientadorAcolhedor · EspecialistaTecnico (espelham PersonaType)
  Scenes/             SalaDeAula.unity (roda no PC e no headset)
  Samples/            PodiumSmokeTest · SmokeTest.unity (cena pronta)
Assets/Tests/EditMode/   WavEncoder · PodiumDto · OrdemDosSlides · CapturaDoMicrofone ·
                         TranscricaoLocal · PainelEmVR · ComandosDoApresentador
                         (43 testes NUnit, sem rede)
Assets/Editor/           PodiumTestSetup · PodiumSlideExporter · PodiumSalaPreview ·
                         PodiumQuestSetup · PodiumSalaSetup · PodiumModeloSetup
Assets/Samples/          Starter Assets do XR Interaction Toolkit (o rig)
Assets/Settings/         assets do URP (PC_RPAsset · Mobile_RPAsset · renderers)
Assets/Plugins/Android/network_security_config.xml
```

**Quatro pacotes, e cada um paga o que custa.** OpenXR e XR Plugin Management porque é o
padrão que a Meta documenta para o Quest — SDK proprietário amarraria o projeto a um
fabricante. XR Interaction Toolkit pelo rig das Starter Assets: controles, raios e
interação com interface são justamente a parte que não compensa reescrever.
[whisper.unity](https://github.com/Macoron/whisper.unity) (MIT) porque traz o whisper.cpp
compilado para ARM64 de Android, que é o que permite transcrever dentro do headset. O
Input System entra junto porque o rig depende dele.

Fora isso, nada: os DTOs continuam em `JsonUtility` e as chamadas em `UnityWebRequest`,
módulos internos do Unity. Um único assembly definition (`Podium.Runtime`), que existe só
para que os testes de EditMode enxerguem o código — asmdef não pode referenciar
`Assembly-CSharp`. Um desenvolvedor só: simplicidade operacional antes de elegância.

## Como rodar a Sala de Aula (sem headset)

A cena `Assets/Podium/Scenes/SalaDeAula.unity` já vem montada e ligada aos assets de
cenário e de persona. Ela funciona com microfone do PC e com a API local.

1. Abra o **Docker Desktop** e espere o ícone ficar verde. Depois suba o backend:

```bash
docker compose -f C:/Users/eduar/projetos/podium/docker-compose.yml up -d
```

2. Confira que a API responde antes de abrir o Unity:

```bash
curl http://localhost:8000/api/v1/health
```

3. No Unity, abra `Assets/Podium/Scenes/SalaDeAula.unity` (duplo clique no Project) e
   dê **Play**. O painel da esquerda verifica a API sozinho; se ela estiver fora do ar,
   a mensagem diz o que fazer.
4. Escolha a persona da banca, confira o caminho do PDF e clique em **Criar sessão**.
5. **Começar apresentação** grava o microfone padrão do Windows. Fale por alguns
   segundos e clique em **Encerrar apresentação**. O último trecho sobe, a análise começa
   e o Feedback Duplo aparece no mesmo painel, com a âncora de cada pergunta.

**A cena parece vazia no Editor, e está tudo certo.** O arquivo salvo tem três objetos —
`Podium`, `Main Camera` e `Directional Light`. Piso, paredes, tela de projeção, púlpito,
banca e plateia são primitivos criados por código em `Start()`, para que o material seja
sempre o do pipeline ativo (URP): cena com material referenciado à mão fica rosa quando o
pipeline muda.

Para ver e ajustar a sala sem dar Play, use **Podium > Pré-visualizar a Sala de Aula** —
ela monta na Scene view com o slide 1 já na parede, e **Podium > Limpar a
pré-visualização da sala** derruba tudo. Nada disso é gravado no arquivo da cena.

O que está na cena e onde mexer:

| Objeto | Componentes | Para quê |
|---|---|---|
| `Podium` | `PodiumApiClient`, `PresentationRecorder`, `PodiumSession`, `SalaDeAulaController`, `SalaDeAulaCenografia`, `ApresentacaoDeSlides` | O fluxo inteiro. O `Controller` decide a sessão e desenha o painel de PC (IMGUI, sem pacote). A `Cenografia` cria chão, púlpito, mesa, tela e um lugar por persona em runtime. A `ApresentacaoDeSlides` acha as imagens e guarda o slide no ar. |
| `Podium` | `WhisperManager`, `TranscritorLocal` | Transcrevem a fala aqui mesmo, enquanto ela acontece. Desligue `ligado` no `TranscritorLocal` para forçar o STT do servidor — é assim que se compara as duas transcrições. |
| `Podium` | `PodiumXRRig`, `PainelEmVR`, `ComandosDoApresentador` | Só entram quando há headset ativo. No PC ficam quietos. |
| `Main Camera` | Camera | Reposicionada no `spawnPosition` do cenário, a 1,6 m de altura. Desligada quando o rig assume. |

### Os slides na parede

**Basta apontar o PDF ou o PPTx** — o mesmo caminho que vai para o `/init`. Não existe
pasta para preparar.

O runtime do Unity não decodifica nenhum dos dois formatos (não há rasterizador de PDF na
engine, e embutir um seria dependência nativa só para desenhar uma parede), então alguém
converte. Aqui é a sua própria máquina, na primeira vez que você aponta o arquivo:

| Formato | Quem rasteriza | Precisa de |
|---|---|---|
| `.pptx` / `.ppt` | PowerPoint da sua máquina, via COM | Office instalado |
| `.pdf` | PyMuPDF do container do backend | `docker compose up -d` na pasta `podium` |

As imagens vão para `Application.persistentDataPath/slides/<arquivo>-<impressão>`, fora
das suas pastas: exportação é subproduto, não arquivo seu. A impressão carrega tamanho e
data de modificação, então cada sessão reaproveita a conversão anterior e um deck editado
é reconvertido sozinho. O PyMuPDF é a mesma biblioteca que extrai o texto dos slides no
backend: página numerada na parede é página numerada na âncora da pergunta.

A conversão roda numa thread à parte, com o painel dizendo o que está acontecendo — leva
segundos no primeiro Play de cada versão do arquivo.

**No headset quem desenha é o backend.** Lá não há nem Office nem Docker, então, quando a
conversão local não produz nada, o cliente pede as páginas ao servidor
(`GET /presentations/{id}/slides`), que as rasteriza com o **mesmo PyMuPDF** que extraiu o
texto — a página 7 da parede continua sendo a página 7 da âncora da pergunta. Só PDF: o
`python-pptx` lê texto, não desenha. A conversão local segue preferida quando existe, por
não depender da rede a cada troca de slide.

Duas saídas continuam valendo, e têm precedência sobre a conversão:

- uma pasta irmã `<arquivo>_slides/` com os PNG, se existir;
- o próprio campo de apresentação apontando direto para uma pasta de imagens.

O menu **Podium > Exportar slides da apresentação** escreve essa pasta irmã quando você
quer as imagens à mão — para conferir o que a banca vê, levar para outra máquina, ou usar
um deck num PC sem Office. Os arquivos são lidos em ordem natural (`slide10` depois de
`slide2`).

Num headset não haverá Docker nem Office: lá quem renderiza as páginas terá de ser o
backend, que já recebe o arquivo no `/init`. Por isso a conversão mora atrás de um único
`ConversorDeSlides.Disponivel`, e não espalhada pela cena.

O botão **Conferir slides** no lobby diz quantas imagens foram encontradas antes de você
criar a sessão. Durante a apresentação, passe de slide pelas setas do teclado, por
PageUp/PageDown, pelo espaço ou pelos botões do painel. A tela grande fica na parede da
frente, para a banca; o **monitor de retorno** no púlpito repete o slide virado para quem
apresenta, que assim não precisa dar as costas para a banca para saber onde está.

Só o slide atual e os dois vizinhos ficam em memória: um deck de 40 páginas carregado
inteiro passaria de 300 MB de textura.

### As perguntas da banca, uma por vez

Terminada a análise, o painel abre no **navegador de perguntas**: uma pergunta em
destaque, a âncora dela logo abaixo (slide de origem e trecho literal) e o porquê da
banca. **Pergunta anterior** e **Próxima pergunta**, ou as mesmas setas do teclado,
andam pela lista.

Trocar de pergunta leva a tela da sala para o `slide_origem` daquela pergunta — a
numeração do backend é a do arquivo que ele leu, e as imagens são a exportação desse
mesmo arquivo. Responder olhando para o slide que gerou a pergunta é o ensaio que
interessa; de quebra, se a pasta de imagens for de outro material, o descompasso aparece
na primeira troca.

**Ver todas as perguntas** volta à lista corrida para conferir tudo de uma vez, e cada
item dela tem **Abrir esta pergunta** para voltar ao modo de uma por vez. Enquanto o
navegador está no ar as setas pertencem às perguntas, não aos slides: duas coisas na
mesma tecla seria surpresa, não atalho.

Uma persona com `avatarPrefab` preenchido é instanciada no seu `seatIndex`; sem prefab,
entra uma cápsula colorida. É esse o ponto de encaixe dos avatares do TCC III.

## Primeiro passo: o teste de fumaça

Antes de qualquer cena, prove que o Unity fala com a API. **Sem headset.**

1. Suba o backend:

```bash
docker compose -f C:/Users/eduar/projetos/podium/docker-compose.yml up -d
```

2. No Unity: `Assets > Create > Podium > API Config`, deixe `baseUrl` em
   `http://localhost:8000` (Editor fala com o PC pela loopback).
3. Numa cena vazia, crie um GameObject e adicione **PodiumSmokeTest**. Aponte o config,
   um PDF de slides e um áudio já gravado (`.mp3`, `.wav`, `.m4a`…).
4. Play. O Console deve percorrer: `health` → sessão criada → áudio recebido →
   `processing` a cada 3 s → Feedback Duplo com as perguntas e suas âncoras.

Uma fala de 10 min leva **cerca de 3 minutos** entre o `/analyze` e o feedback (STT a
~0,28× tempo real + 12–18 s de geração, medidos no backend). Não é travamento.

## Testes

`Window > General > Test Runner`, aba **EditMode**, *Run All*. **43 testes**, em segundos,
sem rede e sem backend: cabeçalho e escala do WAV, leitura dos DTOs a partir de respostas
reais da API (incluindo os nulos que o `JsonUtility` não representa), a montagem da URL, a
ordem natural dos slides, a volta do anel do microfone, o nome do modelo que sobe na
auditoria, a virada do painel de VR para quem apresenta, o resumo do feedback no headset e
as regras dos comandos (para onde vão o A e o B, e como se distingue mão de controle).

Pela linha de comando, sem abrir o Editor:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe" -projectPath C:/Users/eduar/projetos/podium-vr -batchmode -nographics -runTests -testPlatform EditMode -testResults C:/Users/eduar/projetos/podium-vr/Logs/editmode.xml
```

O que os testes **não** cobrem, porque exige microfone, rede ou headset: a captura em si
do `PresentationRecorder`, o `PodiumSession`, o cliente HTTP, a transcrição do whisper e
o que só existe dentro do VR — o raio apertando o botão, a pinça, a vibração. Para os
primeiros existe o teste de fumaça; para o VR, não há substituto para vestir o aparelho
(o roteiro de conferência está em `docs/ANALISE-VR.md`).

## Rede — o que quebra ao sair do Editor

No Editor tudo é `localhost`. **No headset, `localhost` é o próprio Quest.** Três
ajustes, nesta ordem:

1. **Endereço.** `baseUrl` = `http://<IP-do-PC-na-LAN>:8000`.
   Em 2026-09-20 esta máquina estava em **192.168.0.16** — confirme com `ipconfig`, porque o IP
   muda a cada rede. O `docker-compose.yml` já publica a porta 8000 no host.
2. **Firewall do Windows.** Libere a entrada na 8000 (PowerShell como administrador):

```bash
New-NetFirewallRule -DisplayName "Podium API 8000" -Direction Inbound -LocalPort 8000 -Protocol TCP -Action Allow
```

3. **HTTP em texto claro no Android.** Em `Project Settings > Player > Android > Other
   Settings`, coloque **Allow downloads over HTTP** em *Always allowed*. Se ainda assim
   as requisições falharem com erro genérico, ligue **Custom Main Manifest** no mesmo
   painel e adicione ao `<application>`:
   `android:networkSecurityConfig="@xml/network_security_config"` — o arquivo já está em
   `Assets/Plugins/Android/`, bastando trocar o IP lá dentro.

O PC e o headset precisam estar na **mesma rede**. Rede de universidade costuma isolar
clientes entre si; se o `/health` não responder no headset mas responder no PC, o
problema é esse, e um roteador próprio resolve.

## Contrato da API

Base: `http://<host>:8000/api/v1`. Sem autenticação — decisão registrada no PRD do
backend, válida **só** enquanto a API for local. Áudio de voz é dado biométrico (LGPD):
nada de expor essa porta para fora da LAN.

| Rota | O que faz | Retorno |
|---|---|---|
| `GET /health` | Vida da API e do banco | `status`, `environment`, `database` |
| `POST /presentations/init` | Cria sessão, ingere PDF/PPTx | `201` + `session_id` |
| `POST /presentations/{id}/audio` | Áudio inteiro ou chunk (`is_chunk`) | `received_bytes` |
| `PUT /presentations/{id}/transcript` | Entrega a fala já transcrita aqui | `char_count`, `word_count` |
| `POST /presentations/{id}/analyze` | Dispara o pipeline | `202` + `poll_url` |
| `GET /presentations/{id}` | Status da sessão | `created` → `audio_received` → `processing` → `completed` \| `failed` |
| `GET /presentations/{id}/slides` | Quantas páginas o servidor desenha | `total`, `renderizavel`, `motivo` |
| `GET /presentations/{id}/slides/{n}` | Uma página em PNG (`largura`) | imagem |
| `GET /presentations/{id}/feedback` | Feedback Duplo | perguntas + métricas + cobertura + `transcript_source` |

**A ordem do `/transcript` é obrigatória:** depois do último chunk de áudio e antes do
`/analyze`. O `/audio` apaga a transcrição da sessão ao receber áudio novo — é assim que
"existe" significa "é deste áudio" —, então inverter apagaria o que o headset acabou de
mandar. Transcrição vazia recebe `422`: o pipeline não reaproveita vazio, e aceitá-la só
criaria a ilusão de que o STT seria pulado.

`persona` aceita `professor_rigoroso`, `orientador_acolhedor`, `especialista_tecnico`.
`scenario` aceita só `sala_de_aula`. Os valores estão em `SessionStatus.cs` e são
digitados à mão de propósito: o compilador não pode garantir o casamento com o backend,
e um valor inventado deve virar `422` visível, não cenário silenciosamente ignorado.

### Erros que a interface precisa distinguir

Todos já mapeados em `PodiumErrorKind`, com o `detail` da API (em português, acionável)
chegando pronto em `PodiumResult.Message`:

| HTTP | Quando acontece | O que a tela deve dizer |
|---|---|---|
| `415` | Arquivo não é PDF nem PPTx | Peça outro arquivo |
| `422` no init | PDF só com imagens | **Use o `detail` como está** — ele explica reexportar preservando a camada de texto |
| `413` | PDF > 25 MB, ou áudio > 15 min | Limite do MVP |
| `409` no analyze | Análise já rodando | Não é erro: vá para o polling |
| `409` no audio | Chunks com extensões diferentes | Bug do cliente, não do usuário |
| `404` no feedback | `/analyze` ainda não rodou | Estado, não falha |

## Armadilhas verificadas no backend

Lidas no código do `podium`, não supostas:

- **Chunks: um por vez.** `storage_service.next_chunk_path` deriva o índice da *contagem*
  de arquivos, então dois uploads em voo recebem o mesmo índice e um sobrescreve o outro
  — sem erro, com um trecho de fala faltando e todas as métricas de forma contaminadas.
  `PodiumSession` serializa a fila por isso. A correção é a Fase 1.1 do PRD do backend;
  quando entrar, a serialização aqui continua correta, só deixa de ser obrigatória.
- **Todos os chunks na mesma extensão**, ou `409` no meio da gravação.
- **Reprocessar reaproveita a transcrição.** Desde 2026-09-01 o `/analyze` pula o STT
  quando a sessão já tem transcrição; áudio novo no `/audio` a invalida. Trocar de
  persona e reanalisar custa só a geração.
- **WAV está ótimo.** O upload de áudio não tem teto de tamanho (`save_upload` sem
  `max_bytes`) e o backend converte para MP3 mono 16 kHz antes do STT. Por isso
  gravamos direto em 16 kHz mono: é o formato final, sem perda e sem codificador no
  headset.
- **O teto de 15 min é do `/analyze`**, e ele reprova a sessão inteira. O
  `PresentationRecorder` encerra sozinho antes de chegar lá.
- **`slide_coverage` pode vir `null`** em feedback antigo — ausência de medição não é
  cobertura zero. `FeedbackResponse.coverageNaoMedida` preserva a diferença, que o
  `JsonUtility` apagaria.
- **`slide_origem` + `trecho_literal` são obrigatórios em toda pergunta.** São a âncora
  de evidência do sistema; a tela de feedback deve exibi-los junto da pergunta, não
  escondê-los num detalhe. É o que distingue banca ancorada de alucinação.

## Render pipeline: URP

O projeto usa **URP 17.5.0** (pacote embutido no 6000.5.10f1), não o Built-In.

Dois motivos. O Unity 6.5 marcou o Built-In como obsoleto — segue funcionando até o 6.7,
mas é um caminho sem saída para um TCC que atravessa 2026. E o Quest quer URP de
qualquer forma: é onde vivem single-pass instanced, foveated rendering e o suporte de XR
que a Meta documenta.

A migração foi feita agora porque é o momento em que ela custa zero: não há material,
shader nem cena para converter. Feita depois, seria uma tarde inteira de materiais rosa.

Os assets vieram do template *3D Cross-Platform* deste mesmo Editor, com os dois níveis
de qualidade que ele traz:

| Nível de qualidade | Asset | Onde vale |
|---|---|---|
| PC | `Assets/Settings/PC_RPAsset.asset` | Editor e builds desktop |
| Mobile | `Assets/Settings/Mobile_RPAsset.asset` | Android — é o do Quest |

O Android fica no nível **Mobile** (`Project Settings > Quality`), e o *Preparar o projeto
para o Quest* deixa o asset dele com **MSAA 4x, sem HDR e escala 1,0**: o template de
celular vinha sem MSAA, com HDR e a 80% da resolução — no headset, serrilhado cintilando
em toda borda e o texto do painel borrado. A sala fechada também desliga a luz direcional
no Play: com teto ela não ilumina a sala, só vaza pelas junções do teto com as paredes em
faixas serrilhadas (o mapa de sombras não tem resolução para paredes de 20 cm), e custa o
mapa de sombras a cada quadro. No Quest, custo de GPU é custo de conforto.

## Como rodar no Quest

O projeto já vem com OpenXR ligado e com os ajustes de Android aplicados. Do zero, numa
máquina nova, são três comandos de menu e uma build.

1. **`Podium > Preparar o projeto para o Quest`.** Importa as Starter Assets do XR
   Interaction Toolkit, liga o OpenXR em Android e Standalone **com os perfis de
   interação** (Touch, Touch Plus, Touch Pro e o perfil de mão `XR_EXT_hand_interaction`)
   e o *Meta Quest Support* no Android, e ajusta o Player: IL2CPP, ARM64, API mínima 29,
   HTTP em texto claro liberado, Vulkan, qualidade Mobile, entrada nos **dois** sistemas e
   o URP do Quest (MSAA 4x, sem HDR, escala 1,0). Termina listando o que a validação do
   OpenXR reclama. Se ele disser para reiniciar o Editor, reinicie — a troca do sistema de
   entrada só vale depois disso.
2. **`Podium > Baixar o modelo de transcrição`.** Traz o `ggml-base-q5_1.bin` (~57 MB) do
   Hugging Face para `StreamingAssets/Whisper/`. O arquivo não está no repositório e não
   deve entrar: é binário grande, e o Git guardaria uma cópia inteira a cada troca.
3. **`Podium > Ligar a transcrição local e o headset na cena`.** Põe na cena o
   `WhisperManager`, o `TranscritorLocal`, o `PodiumXRRig`, o `PainelEmVR` e o
   `ComandosDoApresentador`, e liga um ao outro. É idempotente: rodar de novo conserta o
   que faltar.
4. **Troque o `baseUrl`** do `PodiumApiConfig` pelo IP do PC na rede (veja *Rede* abaixo).
   `localhost` no headset é o próprio headset.
5. `File > Build Profiles`, plataforma **Android**, e *Build And Run* com o Quest ligado
   no cabo, em modo de desenvolvedor.

Cada um desses passos roda em modo de lote, que é como foram conferidos:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe" -projectPath C:/Users/eduar/projetos/podium-vr -batchmode -nographics -quit -executeMethod Podium.EditorTools.PodiumQuestSetup.Preparar
```

**O que já se viu dentro de um headset, e o que falta.** Em 2026-09-20 um Quest 3 rodou a
cena pelo Link, e o log do Editor mostrou o motivo de não haver raio nem painel utilizável:
`Action Sets: No action sets registered` — nenhum perfil de interação ligado, logo nenhum
controle nem mão chegava ao app. Isso foi corrigido e conferido em modo de lote
(compilação, 43 testes, validação do OpenXR e capturas da visão de quem apresenta, em
`docs/analise-vr/`). Continuam por conferir no aparelho: o raio e a pinça apertando os
botões, a vibração e a velocidade do whisper no XR2. Meça antes de afirmar no relatório —
em especial quantos segundos de atraso a transcrição acumula numa fala de 10 min, porque
é esse número que decide entre o modelo `base` e o `tiny`.

O SDK da Meta (passthrough, malha das mãos) só entra se uma feature específica pedir:
as mãos já funcionam pelo perfil padrão do OpenXR, e OpenXR puro mantém o projeto
portátil.

### Dentro do headset a interface é outra

O painel IMGUI não aparece no Quest — o IMGUI desenha no buffer da tela, e no VR isso não
é lugar nenhum. Quem assume é o `PainelEmVR`: um quadro à **direita** do púlpito, virado
para quem apresenta, com até quatro botões que o raio aperta — pelo gatilho do controle ou
pela pinça da mão. À esquerda fica o monitor de retorno, que taparia os botões, e a banca
fica livre à frente. Mostra a mesma sessão e comanda o mesmo `SalaDeAulaController`,
inclusive o que antes só o PC tinha: *Verificar API* no lobby e o resumo do feedback.

Os comandos que não pedem mira ficam no `ComandosDoApresentador`:

| Comando | Controle | Mão |
|---|---|---|
| Apertar um botão do painel | aponte e aperte o gatilho | aponte e junte indicador e polegar (pinça) |
| Avançar slide ou pergunta | A ou X | botão *Próximo* do painel |
| Voltar slide ou pergunta | B ou Y | botão *Anterior* do painel |
| Trazer o painel para perto | Menu (controle esquerdo) | pinça segurada ~1 s fora do painel |
| Recentralizar a sala | segurar o botão da Meta | gesto de recentralizar do sistema |

A linha de ajuda do painel diz se o headset está lendo controles, mãos ou nada. Uma marca
no chão mostra onde ficar e para onde olhar. O rig não anda pelo analógico nem pula:
quem apresenta fica no púlpito, e o botão A passa slide.

No PC nada disso entra em cena, e o painel da tela continua como antes.

## Fora do escopo por decisão registrada

| Item | Quando entra |
|---|---|
| **Voz da banca (TTS)** | TCC III. Arquitetura já decidida no backend: síntese em lote após a validação, com cache |
| **Avatares da banca** | TCC III. O ponto de encaixe existe: `PersonaDefinition.avatarPrefab` |
| **Autenticação** | Obrigatória antes de qualquer deploy público |
| **Outros cenários** | O MVP entrega só a sala de aula; o backend só conhece `sala_de_aula` |
| **Malha e toque das mãos** | As mãos já apontam e apertam pelo perfil padrão do OpenXR, sem pacote novo. Ver a mão desenhada e apertar botão encostando o dedo pede `com.unity.xr.hands` — só se o teste no aparelho mostrar que o raio não basta |
