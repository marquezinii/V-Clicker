# V-Clicker na Microsoft Store

O pacote MSIX leva **o mesmo `V-Clicker.exe` do release** (mesmos bytes, mesmo SHA-256), sem mudar nada no app. Nada
foi enviado à Store: este documento é o que está pronto e o que falta.

## Gerar o pacote

```powershell
.\build.ps1 -Package                        # dist\V-Clicker.exe (a build reproduzível que o release publica)
.\scripts\msix\build-store-msix.ps1 `
  -PackageName '<Product identity: Package/Identity/Name>' `
  -Publisher '<Package/Identity/Publisher>' `
  -PublisherDisplayName '<Package/Properties/PublisherDisplayName>'
```

O resultado é `dist\store\V-Clicker-Store-<versão>.msix`. **Envie sem assinar**: a Store assina depois da
certificação. A versão vem de `<Version>` em `src\VClicker.csproj` (o quarto número é sempre 0). Sem os três
parâmetros o script usa a identidade do modelo ([`scripts/msix/AppxManifest.xml`](../scripts/msix/AppxManifest.xml)),
boa para teste local, não para o envio: o nome e o publicador têm de ser exatamente os que o Partner Center reserva.

O manifesto declara `runFullTrust` (app WinForms clássico: atalhos globais, `SendInput`, UI Automation),
`allowElevation` (o link "Administrador"), `MinVersion` 10.0.19041.0, o alias `V-Clicker.exe` (a linha de comando do
[modo desenvolvedor](developer.md) funciona de qualquer console) e `neutral` como arquitetura (o app é AnyCPU).

## Testar o pacote

```powershell
powershell -File .\scripts\msix\test-store-msix.ps1               # salvar, lixeira, linha de comando
powershell -File .\scripts\msix\test-store-msix.ps1 -Elevation    # também "Administrador": aceite o UAC
```

Precisa do Modo de Desenvolvedor do Windows e do Windows PowerShell. O script registra o layout
(`dist\store\layout`) como pacote de desenvolvimento, roda as verificações **dentro do pacote** (com o código do próprio
app, por reflexão), remove o pacote e a pasta de teste no fim e não toca em `%APPDATA%\V-Clicker`.

Resultado de 2026-10-22, Windows 11 (26200), build `ca0fe35`: **todas passaram**.

| Verificação | Resultado |
| --- | --- |
| `ClickSettings.Save`/`Load` sobre um `settings.ini` que já existia fora do pacote (quem usava o `.exe` avulso) | ok |
| O mesmo para um arquivo novo, e salvar de novo por cima (`File.Replace`) | ok |
| Renomear macro (`File.Move`), existente fora do pacote e criada dentro | ok |
| Excluir macro para a Lixeira (`FileSystem.DeleteFile`), as duas origens | ok, os itens estão na Lixeira |
| `V-Clicker.exe --help` (0) e argumento inválido (2), pelo caminho e pelo alias | ok |
| A linha que "Copiar comando" produz, rodada contra o `.exe` empacotado | ok (código 4: o app da macro de teste não está aberto) |
| "Administrador": `runas` sobre o `.exe` do pacote, com `allowElevation` | ok: o processo subiu **elevado** e **com identidade de pacote** |

### Pastas de dados

No pacote de teste os arquivos novos de `%APPDATA%` ficaram **na pasta real**, e o cache privado do pacote
(`%LocalAppData%\Packages\<família>\LocalCache`) ficou vazio: um app `runFullTrust` não tem a escrita virtualizada. Então
configurações, macros, perfis e logs continuam onde estão em [developer.md](developer.md), e desinstalar a versão Store
não deve apagá-los. Não foi confirmado numa instalação real da Store. O modo portátil (`V-Clicker.ini` ao lado do `.exe`)
não existe no pacote, porque a pasta de instalação é somente leitura: o app cai sozinho em `%APPDATA%\V-Clicker`.

### Ainda não verificado

- Instalação **assinada, em `WindowsApps`**: o teste usa o layout registrado de uma pasta. O certificado de teste do
  publicador (o do Ralven) venceu em 2026-10-01; um novo precisa ser confiável em `LocalMachine\TrustedPeople`, passo
  que fica com você.
- Windows App Certification Kit (WACK) e Windows 10: nenhum rodou.
- Que a certificação aceite o diálogo de apoio (abaixo).

## Texto para o Partner Center

Submission options > Restricted capabilities (em inglês, como o portal pede). A Store decide; este texto não é uma
aprovação.

**allowElevation**

> V-Clicker is a Windows autoclicker and macro tool. Its executable has an asInvoker manifest and runs at medium integrity: it does not need administrator rights to start, click or run macros, and it installs no service, driver, scheduled task or startup entry. It has no updater; the Microsoft Store owns package updates.
>
> Windows silently drops mouse and keyboard input that a medium-integrity process sends to an elevated window. When the user locks V-Clicker to a program that runs as administrator, the app detects it, refuses to start and shows a "Restart as administrator" link. Only when the user clicks that link does V-Clicker start itself again with the runas verb (the same executable, with the --wait-previous argument), so Windows shows its normal UAC consent prompt; declining it leaves the app as it was, and the first instance closes only after the new one starts. The elevated instance runs the same code and the same features, and ends when the user closes it. Nothing elevates automatically, nothing stays elevated, and UAC is never disabled or bypassed.
>
> The input is generated by the same process that runs the click engine and the global hotkeys, so that process is the one restarted. We request allowElevation only for this user-initiated restart; without it, V-Clicker cannot click inside programs that run as administrator.

**runFullTrust**

> V-Clicker is an existing .NET Framework 4.8 Windows Forms desktop application. It runs as the current user at medium integrity, outside AppContainer, and needs global hotkeys (RegisterHotKey), input injection (SendInput), UI Automation and screen capture of other programs' windows. runFullTrust enables this packaged classic desktop architecture; it does not elevate the app.

**Notas para a certificação**

> No account or sign-in is needed. The optional AI Macro Builder only runs when the user pastes their own API key and asks for a macro; nothing is sent otherwise. The footer "Support" window offers voluntary donations (Ko-fi and a Pix key); it gives nothing in return and unlocks no feature.

## Antes de enviar

1. Reservar o nome no Partner Center e passar a identidade exata ao `build-store-msix.ps1`.
2. URL da política de privacidade: o Macro Builder com IA envia o texto do pedido a `api.anthropic.com` quando o usuário
   o pede.
3. Descrição da loja com as limitações reais (política 10.1.1): é um autoclicker que injeta cliques no Windows; só age em
   programas elevados se o usuário reiniciar como administrador.
4. Decidir o diálogo de apoio. A política [10.8.2](https://learn.microsoft.com/windows/apps/publish/store-policies)
   pede uma API de pagamento terceirizada segura para doações voluntárias; um QR Pix estático dentro do app pode ser
   apontado. Se a certificação devolver por isso, esconder só o diálogo na versão Store resolve.
5. Rodar o WACK e, se possível, o teste com uma instalação assinada.

Referências: [políticas da Store](https://learn.microsoft.com/windows/apps/publish/store-policies),
[capabilities de app](https://learn.microsoft.com/windows/apps/package-and-deploy/app-capability-declarations).
