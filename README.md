# AutoClicker

Autoclicker simples para Windows 64 bits, sem dependências adicionais além do .NET Framework do Windows.

Abra `bin\AutoClicker.exe` ou use o atalho **AutoClicker** na área de trabalho.

- **F6** liga ou desliga, inclusive com outro aplicativo em foco.
- **F8** para imediatamente, inclusive durante o segundo de preparação.
- Ajuste o intervalo enquanto estiver parado: de **20 a 60000 ms**. O padrão de **100 ms** equivale a aproximadamente 10 cliques por segundo.
- Ao iniciar, há **1 segundo** para posicionar o cursor. Os cliques são esquerdos e acompanham a posição atual do cursor.
- Fechar a janela encerra o autoclicker. Minimizar mantém os atalhos funcionando.

O intervalo é aproximado e depende do Windows. Aplicativos elevados ou com proteção contra automação podem bloquear os cliques. F6 e F8 ficam reservados enquanto o programa está aberto; se já estiverem ocupados, o programa informa o conflito.

Para compilar: `powershell -ExecutionPolicy Bypass -File .\build.ps1`.

Para compilar e testar: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test`. O teste abre uma janela de teste, move temporariamente o cursor e verifica cliques reais e atalhos; evite usar o mouse durante os poucos segundos do teste.

A integração usa as APIs oficiais [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) e [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
