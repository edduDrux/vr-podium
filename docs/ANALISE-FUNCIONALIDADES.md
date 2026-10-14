# Análise de funcionalidades — base para o novo podium-vr

**Data:** 2026-10-10 · **Origem:** branch `feat/transcricao-local-e-quest` deste repositório ·
**Backend de referência:** `../podium` (FastAPI)

Objetivo: listar o que o cliente VR atual faz, onde cada coisa mora, o que vale levar e o que
deve mudar, para que o novo projeto já nasça seguindo as documentações da Unity e da Meta.

---

## 1. Resumo em uma página

- O projeto atual é **um único fluxo** (`Idle → Initializing → Ready → Recording → Uploading →
  Analyzing → Completed/Failed`) orquestrado por `PodiumSession`, com duas interfaces: IMGUI no
  PC (`SalaDeAulaController`) e um Canvas em world space no headset (`PainelEmVR`).
- **A transcrição já é local e simultânea à fala** (whisper.cpp via `whisper.unity`, modelo
  `ggml-base-q5_1`). O texto vai para `PUT /transcript` e o `/analyze` pula o STT do servidor.
- **Mas o áudio ainda sobe inteiro**, porque o backend calcula as métricas de forma (pausas,
  ritmo) **a partir do sinal** (`audio_service.analyze_form`, com `pydub.detect_silence`) e o
  `/analyze` devolve `409` se a sessão não tiver áudio. Ou seja: **parar de mandar áudio exige
  mudança no backend** e mover o cálculo das métricas para o headset (seção 4).
- Stack XR atual: OpenXR 1.18 + XR Interaction Toolkit 3.6 (Starter Assets) + Input System +
  URP. Não usa nenhum pacote da Meta. A documentação da Unity recomenda **OpenXR + Unity Meta
  OpenXR** (`com.unity.xr.meta-openxr`); a da Meta recomenda **Meta XR Core SDK + Interaction
  SDK + Building Blocks + Meta XR Simulator** (seção 6).
- Lacuna funcional mais séria para o headset: **não há como escolher o arquivo dentro do
  Quest** — o caminho do PDF é um campo de texto/Inspector com caminho absoluto do PC.

---

## 2. Funcionalidades do usuário

A sua lista, com o estado real e o que muda. Itens marcados com ➕ não estavam na sua lista,
mas existem hoje e precisam de dono no projeto novo.

| # | Funcionalidade | Como funciona hoje | Onde está | Para o projeto novo |
|---|---|---|---|---|
| U1 | **Escolher a persona** | Ciclo entre 3 `PersonaDefinition` (ScriptableObject) com `apiValue` digitado à mão (`professor_rigoroso`, `orientador_acolhedor`, `especialista_tecnico`). Persona fixa no `/init`. Avatar = `avatarPrefab` no `seatIndex`, ou cápsula colorida. | `Scenarios/PersonaDefinition.cs`, `Personas/*.asset`, `SalaDeAulaController.TrocarPersona` | Manter os SOs e o `apiValue` manual (422 visível > valor silenciosamente ignorado). Trocar o ciclo por **cartões** selecionáveis por raio/poke. Ideal: `GET /personas` no backend para não duplicar a lista. |
| U2 | **Selecionar o arquivo** | `TextField` com caminho absoluto (`presentationPath`). Funciona só no PC. No Quest **não há seletor**. | `SalaDeAulaController` linha ~509 | **Decisão aberta** (seção 8). Opções: (a) upload pelo PC/celular para o backend e o headset só lista "minhas apresentações"; (b) Storage Access Framework do Android via plugin nativo; (c) pasta fixa em `/sdcard/Download`. Recomendação: **(a)** — tira do headset upload e conversão. |
| U3 | **Criar sessão** | `POST /presentations/init` multipart (arquivo + persona + cenário). Trata `415`, `422` (PDF só imagem — mostra `detail` como veio), `413`. | `PodiumSession.BeginSession`, `PodiumApiClient.InitSession` | Levar igual. Se U2 virar (a), o `/init` passa a receber um `presentation_id` já enviado em vez do arquivo. |
| U4 | **Controle de slides** | Teclado (setas, PageUp/Down, espaço), botões do painel, **A/X avança, B/Y volta** no controle, gestos de mão. Tela grande na parede + **monitor de retorno** no púlpito. Só slide atual ± 1 em memória. | `Slides/ApresentacaoDeSlides.cs`, `Slides/TelaDeSlides.cs`, `XR/ComandosDoApresentador.cs` | Levar a regra de memória (3 texturas) e o monitor de retorno. Mapear os comandos no **Interaction SDK** (poke/ray/pinch) ou manter Input Actions. |
| U5 | **Encerrar a apresentação** | Botão encerra; o gravador fecha o último pedaço e só então (`RecordingStopped`) a análise é armada. **Parada automática** antes do teto de 15 min. | `PodiumSession.FinishPresentation`, `PresentationRecorder` | Levar a regra "análise só depois do último dado entregue". Com o fluxo novo (seção 4) o "último dado" é o fechamento da transcrição + métricas. |
| U6 | **Navegar pelas perguntas** | Uma pergunta por vez com **âncora** (slide de origem + trecho literal) e o porquê; anterior/próxima; troca de pergunta leva a tela ao `slide_origem`; "Ver todas" e "Abrir esta pergunta". Setas pertencem às perguntas enquanto o navegador está aberto. | `SalaDeAulaController.DesenharNavegadorDePerguntas`, `PainelEmVR` | Levar tudo. A âncora é requisito do projeto (distingue pergunta ancorada de alucinação). |
| ➕U7 | Verificar a API | `GET /health` ao abrir e botão "Verificar API" (inclusive no VR). Mensagem diz o que fazer. | `PodiumApiClient.CheckHealth` | Levar. Tela de conexão com o IP configurável **dentro do app** (hoje está num asset — exige rebuild para trocar de rede). |
| ➕U8 | Conferir slides antes da sessão | Conta as imagens encontradas. Conversão local (PowerPoint COM / PyMuPDF no Docker) ou `GET /slides/{n}` do backend. | `Slides/ConversorDeSlides.cs` | No headset só vale o backend. **Descartar a conversão local** no projeto novo; slides sempre do servidor (mesmo PyMuPDF que gera a numeração das âncoras). |
| ➕U9 | Progresso da análise | Polling de status a cada 3 s com tempo decorrido. | `PodiumApiClient.WaitForCompletion` | Levar. No VR uma tela parada é lida como travamento. |
| ➕U10 | Feedback de forma e cobertura | Métricas (duração, fala efetiva, PPM, pausas), cobertura por slide (`apresentado/parcial/nao_apresentado`, termos ausentes, alerta de descolamento), `transcript_source`. | `PodiumDtos.FeedbackResponse`, painéis de resumo | Levar. Hoje é texto num painel; vale virar uma tela própria. |
| ➕U11 | Nova sessão | Depois do feedback, volta ao lobby para trocar persona e apresentar de novo. | Controller + painel VR | Levar. |
| ➕U12 | Trazer o painel para perto | Menu do controle esquerdo ou pinça longa. | `ComandosDoApresentador`, `PainelEmVR.TrazerParaPerto` | Substituível por um painel que segue o olhar com atraso (padrão da Meta) ou pelo menu de mão. |
| ➕U13 | Modo PC sem headset | Câmera fixa + IMGUI; rig e painel VR ficam inertes. | `PodiumXRRig`, controller | Substituir pelo **Meta XR Simulator** / XR Device Simulator; manter uma única interface (a de VR). |

---

## 3. Lógica de negócio

### N1 — Speech-to-text (hoje)

| Etapa | Implementação |
|---|---|
| Captura | `PresentationRecorder`: `Microphone` a 16 kHz mono, anel drenado a cada quadro. Dois consumidores do mesmo leitor: blocos curtos para o transcritor (`BlocoCapturado`) e pedaços WAV de 60 s para upload (`ChunkReady`). Permissão de microfone pedida no `Start` (Android). |
| Transcrição | `TranscritorLocal`: `WhisperStream` (whisper.unity 1.4.0), VAD simples para não transcrever silêncio (evita alucinações "obrigado", "legendas por..."), texto parcial cresce durante a fala, fechamento com teto de 2 min. |
| Degradação | Sem modelo / sem `WhisperManager` / falha → sessão segue, servidor transcreve. Parcial **nunca** é enviado como se fosse a fala inteira. |
| Entrega | `PUT /presentations/{id}/transcript` **depois do último chunk e antes do `/analyze`** (o `/audio` apaga a transcrição ao receber áudio novo). Vazia = `422`. |

Observação: o streaming do whisper.cpp é **pseudo-streaming** (janelas deslizantes re-inferidas),
não um modelo nativamente incremental. Funciona, mas custa CPU no Quest e o texto parcial pode
reescrever o final. Ver alternativas na seção 4.3.

### N2 — Comunicação com a API

- `PodiumApiClient` é o único que conhece rotas; `PodiumSession` é o único que decide **quando**
  chamar. Corrotinas + `UnityWebRequest` + `JsonUtility` (DTOs em snake_case).
- Resultado tipado `PodiumResult<T>` com `PodiumErrorKind` (`Network`, `Timeout`, `NotFound`,
  `Conflict`, `TooLarge`, `UnsupportedMedia`, `Unprocessable`, `Server`) e o `detail` do FastAPI
  pronto para a tela (inclusive quando `detail` é lista do Pydantic).
- Robustez: uploads de chunk **serializados** (backend deriva o índice pela contagem de
  arquivos — dois em voo se sobrescrevem), 3 tentativas só para falha de transporte, polling que
  tolera oscilação de Wi-Fi, `409` no `/analyze` desambiguado pelo status da sessão.
- Armadilhas do `JsonUtility` já contornadas: `slide_coverage: null` vira objeto vazio (detectado
  no corpo bruto), `float` nulo vira `0`.

Contrato atual (base `/api/v1`, sem autenticação — só LAN):

| Rota | Uso |
|---|---|
| `GET /health` | conectividade |
| `POST /presentations/init` | cria sessão + ingere PDF/PPTx |
| `POST /presentations/{id}/audio` | áudio inteiro ou chunk (`is_chunk`) |
| `PUT /presentations/{id}/transcript` | transcrição feita no headset |
| `POST /presentations/{id}/analyze` | dispara pipeline (`202`) |
| `GET /presentations/{id}` | status (`created → audio_received → processing → completed/failed`) |
| `GET /presentations/{id}/slides` e `/slides/{n}` | páginas renderizadas pelo servidor (só PDF) |
| `GET /presentations/{id}/feedback` | Feedback Duplo |

### N3 — Enviar, receber e exibir

Estados da sessão espelham os do backend para a tela dizer a verdade. Eventos públicos:
`StateChanged`, `AnalysisProgress`, `FeedbackReady`, `SessionFailed`. A interface só escuta
eventos — bom desenho para levar.

---

## 4. A mudança pedida: mandar só o texto, transcrito enquanto a pessoa fala

### 4.1 O que já é verdade

A transcrição **já** acontece durante a fala e **já** é enviada pronta. O ganho que falta não
está no STT, está em:

1. o upload do áudio (WAV 16 kHz mono ≈ 1,9 MB/min; 15 min ≈ 29 MB pelo Wi-Fi do headset);
2. o backend ainda normalizar e analisar esse áudio para as métricas de forma;
3. o fechamento do whisper (até o último trecho ser inferido).

### 4.2 O bloqueio

| Onde | O que impede mandar só texto |
|---|---|
| `podium/app/api/v1/endpoints/presentations.py` (~linha 234) | `/analyze` responde `409` se não houver `audio_path` nem chunks. |
| `podium/app/services/analysis_service.py` (~linha 122) | `audio_service.analyze_form(presentation.audio_path, transcript)` — pausas e PPM vêm do sinal. |
| Invariante do projeto | "métricas de forma derivam do sinal, independentes do STT". |

### 4.3 Proposta

**Calcular as métricas de forma no headset, a partir do mesmo sinal, e mandar texto + métricas.**
O invariante continua de pé (as pausas ainda vêm do sinal, não do texto); só muda onde o cálculo
roda.

| Peça | No headset | No backend (mudança) |
|---|---|---|
| Pausas | VAD por energia sobre o PCM, mesma regra do backend (silêncio ≥ `MIN_PAUSE_MS`, limiar relativo ao volume) — portar `_silence_threshold` + `detect_silence` | Aceitar `metrics` no corpo e **validar faixas** (duração ≤ 15 min, pausas ≤ duração) |
| Duração / fala efetiva | relógio da captura − pausas | — |
| Palavras / PPM | contar no texto final | pode recalcular a partir do texto (barato) |
| Transcrição | incremental, segmento a segmento | novo `POST /presentations/{id}/transcript/segments` (opcional) ou o `PUT` atual no fim |
| Análise | dispara assim que o último segmento fecha | `/analyze` aceita sessão **sem áudio** se houver transcrição + métricas; `metrics_source = "cliente_vr"` no feedback, como já existe `transcript_source` |
| Áudio | **opcional**: guardar local e subir depois, em segundo plano, só para auditoria/validação do TCC | manter a rota, fora do caminho crítico |

Ganho extra de mandar **segmentos durante a fala**: o backend pode adiantar trabalho
(cobertura de slides parcial, contexto) e o `/analyze` final só faz a geração das perguntas.

**Motor de STT** — opções para o projeto novo, em ordem de recomendação:

| Opção | Prós | Contras |
|---|---|---|
| whisper.cpp (`whisper.unity`), como hoje | já validado no projeto, offline, PT-BR bom, MIT | pseudo-streaming, CPU alta no Quest com modelo maior que `base` |
| Modelo nativamente streaming (ex.: sherpa-onnx com Zipformer/Paraformer) | parcial estável, baixa latência, offline | precisa de modelo PT-BR de qualidade; integração nativa Android própria — **validar antes** |
| Meta Voice SDK (Dictation, Wit.ai) | integrado ao ecossistema Meta, streaming | **nuvem** (áudio de voz é dado biométrico/LGPD), dependência de conta Wit — validar idioma e limites |

Recomendação: começar com whisper.cpp (risco conhecido) atrás de uma interface
`ITranscritor { Iniciar; Alimentar; TextoParcial; SegmentoFechado; Encerrar }` para poder trocar
o motor sem tocar na sessão.

---

## 5. O que levar do projeto atual (decisões que se provaram)

- Separação **cliente HTTP (como) × sessão (quando) × interface (só escuta eventos)**.
- `PodiumResult<T>` + `PodiumErrorKind` + mensagens acionáveis do backend.
- `apiValue` de persona/cenário digitado à mão; DTOs espelhando o contrato literalmente.
- Degradação sem inventar: falha da transcrição local nunca derruba a sessão; parcial nunca é
  vendido como final; ausência de medição ≠ medição zero.
- Slides: só atual ± 1 em memória; numeração do servidor = numeração das âncoras.
- Os 43 testes de EditMode (WAV, DTOs a partir de respostas reais, ordem dos slides, regras de
  comando) — portar os que continuarem fazendo sentido.

## 6. Mapeamento para a stack recomendada pela Unity e pela Meta

| Necessidade | Hoje | Unity (docs Unity 6) | Meta (Horizon docs) | Sugestão |
|---|---|---|---|---|
| Provedor XR | OpenXR 1.18 | OpenXR + **Unity Meta OpenXR** (`com.unity.xr.meta-openxr`); Oculus XR plugin em migração para OpenXR | Meta XR Core SDK | OpenXR como provedor + pacotes Meta por cima |
| Rig / câmera | Starter Assets do XRI, instanciado em runtime | XR Origin (XRI) | `OVRCameraRig` / Building Blocks | Rig **salvo na cena**, não montado por código (evita os defeitos 7–8 da `ANALISE-VR.md`) |
| Interação controle + mão | XRI + Input Actions próprias + `XR_EXT_hand_interaction` | XRI | **Interaction SDK** (ray, poke, pinch, UI prontos) | Interaction SDK se seguir Meta; XRI se priorizar portabilidade |
| UI no mundo | Canvas montado por código, IMGUI no PC | uGUI/UI Toolkit world space | ISDK + Building Blocks de UI | Canvas como **prefab** desenhado no Editor |
| Testar sem headset | modo PC com IMGUI | XR Device Simulator | **Meta XR Simulator** | Simulator; descartar a interface de PC |
| Voz | whisper.unity | — | Voice SDK | ver 4.3 |
| Setup de projeto | menu `Podium > Preparar o projeto para o Quest` (414 linhas) | Project Validation do XR Plug-in Management | Project Setup Tool do Core SDK | usar as ferramentas oficiais; não reescrever o setup |
| Render | URP, Mobile_RPAsset ajustado (MSAA 4x, sem HDR, escala 1,0, Vulkan) | URP | URP + foveated rendering | começar do **template VR** da Unity 6 e reaplicar os ajustes |

Templates citados na doc da Unity: `com.unity.template.vr`, `com.unity.template.mixed-reality`.

## 7. Armadilhas já pagas (não repetir)

| Armadilha | Sintoma | Fonte |
|---|---|---|
| Canvas world space se lê por −Z | painel de costas, texto espelhado | `ANALISE-VR.md` §3 |
| Perfis de interação OpenXR desligados | `Action Sets: No action sets registered` — sem controle nem mão | `ANALISE-VR.md` §4 |
| "Meta Quest Support" desligado no Android | app não abre como VR | `ANALISE-VR.md` §4 |
| `localhost` no headset é o Quest | `Curl error 7` | README › Rede |
| HTTP em texto claro bloqueado no Android | erro genérico de rede | README › Rede; `Plugins/Android/network_security_config.xml` |
| Microfone sem permissão | `Microphone.devices` vazio | commit `f8ed26b` |
| Dois `Microphone.Start` no mesmo dispositivo | um consumidor fica mudo | `PodiumSession` (um leitor, dois consumidores) |
| Chunks em paralelo | trecho perdido sem erro | README › Armadilhas |
| `JsonUtility` e `null` | 0% de cobertura em sessão não medida | `PodiumApiClient.CoberturaNaoMedida` |
| Rede de universidade isola clientes | `/health` responde no PC e não no headset | README › Rede |
| Textura de 40 slides inteira | > 300 MB | README › Slides |

## 8. Decisões a tomar antes de começar

1. **Seletor de arquivo no Quest** (U2): upload pelo backend (recomendado), SAF nativo ou pasta fixa.
2. **Meta Interaction SDK ou XRI** para a interação — define quanto do projeto fica preso à Meta.
3. **Motor de STT** (4.3) e se a transcrição sobe por segmentos ou no fim.
4. **Contrato novo do backend**: `/analyze` sem áudio + `metrics` do cliente + `metrics_source`.
5. **Áudio**: some de vez, ou sobe em segundo plano só para auditoria do TCC (comparar STT local × servidor exige o áudio).
6. **Endereço da API configurável dentro do app** (hoje exige rebuild a cada rede).

## 9. Ordem sugerida para o projeto novo

1. Template VR da Unity 6 + pacotes Meta + Project Setup Tool → build vazia rodando no Quest.
2. Rig e painel world space como prefabs; navegação de telas (Conexão → Lobby → Sala → Feedback) no Simulator.
3. Portar `PodiumApiClient`, DTOs, `PodiumResult` e os testes de EditMode.
4. Captura de microfone + `ITranscritor` (whisper) + métricas de forma no cliente, com testes do VAD contra o resultado do `pydub` em áudios reais.
5. Mudança do backend (decisão 4) e fluxo "texto + métricas".
6. Slides do servidor na parede + monitor de retorno + comandos.
7. Feedback: navegador de perguntas com âncora, métricas e cobertura.
8. Personas com avatar (o encaixe `avatarPrefab` + `seatIndex` continua valendo).
