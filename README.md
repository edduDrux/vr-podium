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

Esqueleto de integração. **Existe:** o cliente HTTP completo das cinco rotas, gravação
de microfone em pedaços, a máquina de estados da sessão e um teste de fumaça que roda o
fluxo inteiro no Editor. **Não existe:** cena, avatares, interação XR, TTS.

Isso é deliberado — a integração é o risco técnico do projeto, e ela cabe num teste de
Editor. Modelar sala de aula não é o TCC.

## Estrutura

```
Assets/Podium/
  Runtime/Api/        PodiumApiClient · DTOs · PodiumApiConfig · PodiumResult
  Runtime/Audio/      PresentationRecorder (chunks) · WavEncoder
  Runtime/Session/    PodiumSession (orquestra tudo) · SessionStatus · PersonaValues · ScenarioValues
  Runtime/Scenarios/  ScenarioDefinition · PersonaDefinition (ScriptableObjects)
  Scenarios/          assets de cenário (crie via Assets > Create > Podium > Cenário)
  Personas/           assets de persona  (Assets > Create > Podium > Persona)
  Samples/            PodiumSmokeTest · SmokeTest.unity (cena pronta)
Assets/Tests/EditMode/   WavEncoderTests · PodiumDtoTests (NUnit, sem rede)
Assets/Editor/           PodiumTestSetup (menu Podium > Preparar cena de teste)
Assets/Settings/         assets do URP (PC_RPAsset · Mobile_RPAsset · renderers)
Assets/Plugins/Android/network_security_config.xml
```

Nenhum pacote externo: os DTOs usam `JsonUtility` e as chamadas usam `UnityWebRequest`,
ambos módulos internos do Unity. Um único assembly definition (`Podium.Runtime`), que
existe só para que os testes de EditMode enxerguem o código — asmdef não pode referenciar
`Assembly-CSharp`. Um desenvolvedor só: simplicidade operacional antes de elegância.

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

`Window > General > Test Runner`, aba **EditMode**, *Run All*. Rodam em segundos, sem
rede e sem backend: cabeçalho e escala do WAV, leitura dos DTOs a partir de respostas
reais da API (incluindo os nulos que o `JsonUtility` não representa) e a montagem da URL.

Pela linha de comando, sem abrir o Editor:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe" -projectPath C:/Users/eduar/projetos/podium-vr -batchmode -nographics -runTests -testPlatform EditMode -testResults C:/Users/eduar/projetos/podium-vr/Logs/editmode.xml
```

O que os testes **não** cobrem, porque exige microfone ou rede: `PresentationRecorder`,
`PodiumSession` e o cliente HTTP. Para esses existe o teste de fumaça.

## Rede — o que quebra ao sair do Editor

No Editor tudo é `localhost`. **No headset, `localhost` é o próprio Quest.** Três
ajustes, nesta ordem:

1. **Endereço.** `baseUrl` = `http://<IP-do-PC-na-LAN>:8000`.
   Hoje esta máquina está em **10.120.8.241** — confirme com `ipconfig`, porque o IP
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
| `POST /presentations/{id}/analyze` | Dispara o pipeline | `202` + `poll_url` |
| `GET /presentations/{id}` | Status da sessão | `created` → `audio_received` → `processing` → `completed` \| `failed` |
| `GET /presentations/{id}/feedback` | Feedback Duplo | perguntas + métricas + cobertura |

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

Ao montar a cena e ligar o XR, confirme que a plataforma Android está no nível **Mobile**
(`Project Settings > Quality`), e ajuste ali MSAA e sombras: no Quest, custo de GPU é
custo de conforto.

## Pacotes XR — adicione quando for construir a cena

Nada de XR está instalado ainda (o esqueleto não precisa). Em `Window > Package Manager`:

- **XR Plugin Management** — habilite **OpenXR** em Android e no Standalone
- **OpenXR Plugin** — ative o grupo de features do Meta Quest
- **XR Interaction Toolkit** — importe as samples *Starter Assets* e
  **XR Device Simulator** (é ele que permite testar sem headset)

Build para Quest: plataforma **Android**, **IL2CPP**, **ARM64**. A API mínima segue a
exigência vigente da Meta — confira antes de empacotar. O SDK da Meta (passthrough, hand
tracking) só entra se uma feature específica pedir; OpenXR puro mantém o projeto portátil.

## Fora do escopo por decisão registrada

| Item | Quando entra |
|---|---|
| **Voz da banca (TTS)** | TCC III. Arquitetura já decidida no backend: síntese em lote após a validação, com cache |
| **Autenticação** | Obrigatória antes de qualquer deploy público |
| **Outros cenários** | O MVP entrega só a sala de aula; o backend só conhece `sala_de_aula` |
