# Etapa casual — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Window Lock, ícone da bandeja por estado, mini-HUD e CPS direto (solicitado vs. enviado) no V-Clicker.

**Architecture:** O motor (`ClickEngine`) ganha uma fase `Paused` e uma verificação injetável `ClickPlan.Lock`
(`Func<Point?, LockStatus>`), consultada antes de cada clique e nas fatias de espera; tudo o mais (achar janelas,
foco, HUD, ícones) fica fora do motor. O intervalo vira `double` de ponta a ponta; a interface edita um único valor
canônico (`ClickSettings.IntervalMs`) por dois modos de exibição.

**Tech Stack:** C# 12, WinForms, .NET Framework 4.8, P/Invoke Win32. Sem dependências novas.

**Spec:** `docs/superpowers/specs/2026-10-21-etapa-casual-design.md`

## Global Constraints

- Sem dependências de terceiros; um único `.exe`; `TreatWarningsAsErrors` continua ligado.
- Todo texto novo em PT e EN em `src/Loc.cs` (o teste de localização exige os dois e placeholders iguais).
- `.ini` antigo continua abrindo igual; leitura nunca lança; números com cultura invariável.
- Limite de pausa: **5 minutos**, fixo. CPS: **0,1 a 1000**, uma casa. ms: até **3 casas**.
- Atalhos `5 · 10 · 15 · 20 · 50 CPS`. HUD padrão: ligado, canto superior direito. Window Lock padrão: desligado.
- Nada de ganchos de entrada, injeção, captura de tela.
- Sem commits (o usuário não pediu); a verificação é `.\build.ps1 -Test`.

## Review Focus

1. Identificador de janela reaproveitado por outro processo depois que o jogo fecha → tem que contar como "fechou" (teste em Task 3).
2. `.ini` com `IntervalMs=NaN`, `Infinity`, `66,667` (vírgula) ou valor negativo → cai no padrão ou é limitado, nunca lança (Task 1).
3. Stop/F8 pedido durante a pausa ou durante a contagem congelada → encerra na hora, sem clicar (Task 2).
4. Suspensão/bloqueio do Windows com a sessão pausada → encerra; nunca retoma ao acordar (Task 4, motivo `Suspended` testado no motor em Task 2).
5. HUD sobre o ponto fixo → o clique atravessa e o HUD nunca vira primeiro plano (estilos verificados em Task 6).

---

### Task 1: Intervalo fracionário e CPS

**Files:** Modify `src/Interval.cs`, `src/Settings.cs`, `src/ClickEngine.cs` (só `IntervalMs`/`MsToTicks`), `src/Controls.cs` (`NumberField`), `src/MainForm.cs` (card de velocidade, painel), `src/Loc.cs`; Test `tests/VClicker.Tests/UnitTests.cs`.

**Interfaces — Produces:**
- `Interval.Clamp(double ms) : double` (NaN → `MinMs`), `Join(int h, int m, int s, double ms) : double`,
  `Split(double total, out int h, out int m, out int s, out double ms)`, `ClicksPerSecond(double ms) : double`,
  `FromCps(double cps) : double`, `const double MinCps = 0.1, MaxCps = 1000`.
- `ClickSettings.IntervalMs : double` (gravado com `"R"`), `ClickSettings.SpeedInCps : bool = true`.
- `ClickPlan.IntervalMs : double`.
- `NumberField.Decimals : int`, `NumberField.Number : double`, `NumberField.ShowText(string)`; `Value : int` continua.

- [ ] Testes: `interval: fractional split/join` (Split(66.6667) → ms≈66.6667; Join(0,0,1,0.5)=1000.5);
  `interval: cps conversion` (FromCps(15)==1000.0/15; ClicksPerSecond(1000.0/15)≈15; FromCps(0.01) limitado a MaxMs);
  `settings: fractional interval round-trips exactly` (1000.0/15 salvo e lido igual); `settings: old integer and bad
  interval values` ("100"→100, "NaN"/"Infinity"/"abc"→100/limite, "-5"→1); `SpeedInCps` no round-trip.
- [ ] Rodar `.\build.ps1 -Test` → falha de compilação/teste esperada.
- [ ] Implementar. `ReadUi` deixa de recalcular `IntervalMs` a partir dos campos; só `SetInterval(double)` (chamado por
  edição de campo, atalho ou carga) muda o valor canônico. Card: `Seg("speed.cps","speed.interval")`, linha CPS
  (`fCps`, `Decimals=1`, unidade "CPS") e linha h/min/s/ms (`fMillis.Decimals=3`), uma visível por vez; rótulo de
  conversão (`≈ 66,67 ms`, `· 30 cliques/s` com Duplo; `< 0,1` + dica no modo CPS quando abaixo do mínimo).
  Painel: 4 blocos `CLIQUES · SOLICITADO · ENVIADO · TEMPO` + nota de honestidade; ENVIADO `—` parado/pausado.
- [ ] `.\build.ps1 -Test` → PASS.

### Task 2: Motor — pausa, limite, motivos

**Files:** Modify `src/ClickEngine.cs`; Test `tests/VClicker.Tests/UnitTests.cs`.

**Interfaces — Produces:**
- `enum LockStatus { Ok, NotFocused, PointOutside, Closed }`.
- `EnginePhase.Paused`; `StopReason.FocusLost, TargetClosed, PauseTimeout, Suspended`.
- `ClickPlan.Lock : Func<Point?, LockStatus>` (null = sem lock), `ClickPlan.StopOnFocusLoss : bool`,
  `ClickPlan.PauseLimitMs : int = 300000`.
- `ClickEngine.Stop(StopReason reason = StopReason.Manual)`, `ClickEngine.SessionElapsed : TimeSpan`,
  `ClickEngine.PauseCause : LockStatus` (válido em `Paused`). `Elapsed` = tempo clicando, sem pausas.

- [ ] Testes (lock falso controlado por variável): `engine: lock pauses and resumes without a burst`;
  `engine: closed target stops as TargetClosed`; `engine: strict mode stops as FocusLost`;
  `engine: point outside pauses even in strict mode`; `engine: pause limit stops as PauseTimeout` (`PauseLimitMs=150`);
  `engine: Stop during pause ends at once` (Manual e `Stop(Suspended)` → Suspended, 0 cliques depois);
  `engine: countdown freezes while paused` (delay 150 ms, 200 ms sem foco → 1º clique ≥ 330 ms);
  `engine: Elapsed excludes pauses, SessionElapsed includes them`.
- [ ] Falham → implementar: `AwaitLock` antes de cada clique (pausa em laço de 25 ms; ao retomar, agenda recomeça em
  `now`); `WaitUntil` retorna cedo se o lock deixar de estar `Ok`; contagem regressiva em fatias, descontando só o tempo
  ativo; motivo de `Stop(reason)` usado quando o cancelamento é observado.
- [ ] `.\build.ps1 -Test` → PASS.

### Task 3: Janelas do Windows (`src/WindowLock.cs`)

**Interfaces — Produces:** `sealed class AppWindow { IntPtr Handle; uint ProcessId; string ExePath, ExeName, DisplayName, Title; }`;
`static class WindowLock { List<AppWindow> List(string exeFilter = null); AppWindow FromHandle(IntPtr); IntPtr Foreground();
LockStatus Check(IntPtr window, uint processId, Point? at); bool BringToFront(IntPtr window); }`. P/Invoke novos em `Native.cs`.

- [ ] Testes: `window lock: zero or destroyed handle is Closed`; `window lock: handle owned by another process id is Closed`;
  `window lock: lists a visible test window with its exe and skips V-Clicker's own windows filter`.
- [ ] Implementar (filtros: visível, com título, sem dono, sem `WS_EX_TOOLWINDOW`, não "cloaked", não `ApplicationFrameHost.exe`).
- [ ] `.\build.ps1 -Test` → PASS.

### Task 4: Window Lock na interface

**Files:** Modify `src/MainForm.cs`, `src/Settings.cs`, `src/Loc.cs`; Create `src/WindowPicker.cs`; Test `UnitTests.cs`.

**Consumes:** Tasks 2–3. **Produces:** settings `WindowLock`, `LockExe`, `LockName`, `LockStopOnFocusLoss`.

- [ ] Testes: `settings: window lock fields round-trip and bad exe names are dropped` ("..\\x.exe", "a/b.exe", "x.txt" → vazio).
- [ ] Implementar card (check, nome + "Escolher…", `Seg("lock.pause","lock.stop")`), `WindowPicker` (lista escura com
  ícone), fluxo de início (F6: só com o app em primeiro plano; botão/bandeja: 0 → recusa, >1 → picker filtrado,
  `BringToFront` + confirmação; ponto fixo fora → recusa), `SystemEvents` (suspensão, bloqueio) → `Stop(Suspended)`,
  mensagens por `StopReason`, bipe de erro.
- [ ] `.\build.ps1 -Test` → PASS.

### Task 5: Ícone da bandeja por estado

- [ ] Ícones verde/amarelo gerados a partir do ícone do app (ponto colorido), trocados só quando o estado muda; dica com o detalhe (≤ 63 caracteres). Teste: `tray: state icons are distinct and disposed`.

### Task 6: Mini-HUD (`src/Hud.cs`)

**Produces:** `sealed class Hud : Form { void Set(Color dot, string text, Screen screen, HudCorner corner); void FlashStopped(string text); }`, `enum HudCorner { TopRight, TopLeft, BottomRight, BottomLeft }`, settings `Hud`, `HudCorner`.

- [ ] Teste: `hud: never activates and lets clicks through` (estilos `WS_EX_TRANSPARENT|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_TOPMOST`, `ShowWithoutActivation`); `settings: hud fields round-trip`.
- [ ] Implementar; HUD só existe após a primeira tentativa de início; atualizado pelo `uiTimer` só quando muda.
- [ ] `.\build.ps1 -Test` → PASS.

### Task 7: Integração, docs e revisão

- [ ] Teste de integração: janela-alvo vinculada; ao ativar outra janela, nenhum clique chega a ela; ao voltar, retoma.
- [ ] README (PT/EN), CHANGELOG (`[Unreleased]`), ROADMAP sem alterações de escopo.
- [ ] `.\build.ps1 -Test` e revisão final do diff por um revisor independente.
