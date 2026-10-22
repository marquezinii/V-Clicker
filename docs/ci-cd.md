# CI/CD do V-Clicker

Tudo roda no GitHub Actions, a partir de `.github/workflows/`. Cada passo da CI é um comando do `build.ps1`, então o
que a CI checa você reproduz na sua máquina com o mesmo comando.

## Workflows

| Workflow | Quando roda | O que faz |
| --- | --- | --- |
| [`ci.yml`](../.github/workflows/ci.yml) | push na `main`, pull requests, manual | Lint, testes, cobertura, integração, build reproduzível, capturas de tela; publica a pré-release `dev` (só `main`) e comenta nos PRs |
| [`codeql.yml`](../.github/workflows/codeql.yml) | push, PR, segundas-feiras | Análise estática de segurança do C# (suíte *security-and-quality*) → **Security › Code scanning** |
| [`scorecard.yml`](../.github/workflows/scorecard.yml) | push na `main`, terças-feiras | [OpenSSF Scorecard](https://scorecard.dev/viewer/?uri=github.com/marquezinii/V-Clicker): saúde da cadeia de suprimentos do repositório |
| [`links.yml`](../.github/workflows/links.yml) | PR/push que mexe em `.md`, segundas-feiras | Todo link dos Markdown precisa resolver (lychee) |
| [`labeler.yml`](../.github/workflows/labeler.yml) | PR aberto ou atualizado | Rotula o PR pelos caminhos alterados ([`.github/labeler.yml`](../.github/labeler.yml)); as notas de release agrupam por esses rótulos ([`.github/release.yml`](../.github/release.yml)) |
| [`release-prepare.yml`](../.github/workflows/release-prepare.yml) | manual | Sobe a versão, data o CHANGELOG, faz commit e tag e dispara o release |
| [`release.yml`](../.github/workflows/release.yml) | tag `v*` (ou manual numa tag) | Compila, testa, empacota, gera SBOM, assina, atesta a procedência e publica a release |

[Dependabot](../.github/dependabot.yml) abre um PR por semana com as actions (fixadas por SHA de commit, com o
comentário da versão) e as ferramentas .NET; a própria CI faz o *squash-merge* desse PR quando todos os jobs passam.

## O que a CI checa (`ci.yml`)

| Job | Comando local | Detalhes |
| --- | --- | --- |
| **Lint** | `.\build.ps1 -Lint` | `dotnet format --verify-no-changes` (espaços, estilo e corretores de analyzers, conforme [`.editorconfig`](../.editorconfig)); PSScriptAnalyzer em `build.ps1` e `scripts/` ([`PSScriptAnalyzerSettings.psd1`](../PSScriptAnalyzerSettings.psd1)); depois a build, em que os analyzers do .NET rodam no nível *recommended* e todo aviso é erro ([`Directory.Build.props`](../Directory.Build.props)) |
| **Lint workflows** | `actionlint` | Os próprios arquivos de workflow (sintaxe, expressões, shellcheck dos passos em bash) |
| **Unit tests** | `.\build.ps1 -Test` | Os testes unitários em `windows-2022` e `windows-2025`; o resumo do job lista o que falhou |
| **Coverage** | `.\build.ps1 -Test -Coverage` | Os mesmos testes sob o `dotnet-coverage`, numa build à parte com PDB (`TestResults\coverage-build`; o `.exe` distribuído não tem PDB e o coletor só mede módulos com símbolos). Relatório HTML em `TestResults\coverage\index.html` (artefato `coverage-report`), resumo no job e no comentário do PR |
| **Integration tests** | `.\build.ps1 -Integration` | Os testes que movem o mouse e usam atalhos de verdade, na área de trabalho do runner (1024×768, escala 100 %). Se falhar, uma captura da tela do runner vai no artefato `integration-tests` |
| **Package** | `.\build.ps1 -Package` | Compila duas vezes (a segunda numa árvore limpa) e exige que os dois `V-Clicker.exe` tenham o mesmo SHA-256 (build determinística); empacota; **smoke test**: o `.exe` empacotado precisa responder `--help` com código 0 e um argumento inválido com 2. Artefato `V-Clicker-dist` |
| **Screenshots** | `V-Clicker.Tests.exe --screenshot …` | Renderiza a janela principal (pt/en, simples, rodando), o Macro Studio, Perfis, Apoiar e Criar com IA para PNG, com um *sink* de cliques nulo; artefato `screenshots`, para revisar mudanças visuais sem compilar |
| **PR comment** | — | Um comentário (sempre o mesmo, atualizado) com o link para baixar a build do PR e a cobertura. Só para PRs do próprio repositório: PRs de forks têm token só de leitura |
| **Rolling dev pre-release** | — | A cada push na `main` que passa em tudo, a pré-release [`dev`](https://github.com/marquezinii/V-Clicker/releases/tag/dev) é recriada com essa build. O `.exe` leva a versão com sufixo `-dev.<número da execução>` nas propriedades do arquivo (`InformationalVersion`, lida do ambiente pelo MSBuild) |
| **Merge Dependabot PRs** | — | `gh pr merge --squash` nos PRs do Dependabot depois de todos os jobs acima |

O token do workflow começa só com leitura (`permissions: contents: read`); cada job pede só o que precisa.

### Rodar localmente

Requisitos: [.NET SDK](https://dotnet.microsoft.com/download) (a CI usa 10.x) e, para `-Lint`,
`Install-Module PSScriptAnalyzer -Scope CurrentUser`. `-Coverage` restaura as ferramentas fixadas em
[`.config/dotnet-tools.json`](../.config/dotnet-tools.json) (`dotnet-coverage`, ReportGenerator, sbom-tool).

```powershell
.\build.ps1 -Lint -Test -Coverage -Package   # o que a CI roda, menos os testes de integração e as capturas
.\build.ps1 -Integration                     # move o cursor por alguns segundos: não toque no mouse
```

`actionlint` para os workflows: `docker run --rm -v "${PWD}:/repo" --workdir /repo rhysd/actionlint:1.7.12 -color`
(ou o binário de <https://github.com/rhysd/actionlint/releases>).

### Formatação e analyzers

- `.editorconfig` manda: LF, UTF-8 sem BOM, 4 espaços. Inicializadores de objeto podem ter vários membros na mesma
  linha e as chaves de um bloco `case` ficam na coluna do rótulo (o código faz isso de propósito).
- `.gitattributes` faz o *checkout* em LF em qualquer sistema (`* text=auto eol=lf`), senão o `dotnet format` falharia
  no Windows por causa do CRLF. Num clone antigo, `git add --renormalize .` (ou um clone novo) alinha a árvore.
- Os analyzers do .NET rodam no nível *latest-recommended* e todo aviso é erro. As regras que o código ainda não
  cumpria quando o portão entrou (CA1305, CA1310, CA1806, CA1822, CA1825, CA1838, CA1859, CA1861, CA2201, CA2263)
  estão como *suggestion* no `.editorconfig`, com o motivo. Para subir uma: corrija os achados dela e apague a linha.

## Lançar uma versão

O CHANGELOG precisa ter o que mudou em **[Unreleased]**; a versão nova precisa ser maior que a atual
(`<Version>` em `src/VClicker.csproj`). Dois caminhos:

1. **Um comando** (sem clone local):

   ```powershell
   gh workflow run release-prepare.yml -f version=1.1.0
   ```

   O workflow roda `scripts/prepare-release.ps1`, faz o commit `Release v1.1.0` na `main`, cria a tag `v1.1.0` e dispara
   o `release.yml` nela (uma tag enviada pelo token do workflow não dispara outros workflows sozinha, por isso o
   `gh workflow run`).

2. **Local**:

   ```powershell
   .\scripts\prepare-release.ps1 -Version 1.1.0      # só edita src\VClicker.csproj e CHANGELOG.md
   git commit -am "Release v1.1.0"
   git tag -a v1.1.0 -m "V-Clicker v1.1.0"
   git push origin main v1.1.0                      # a tag dispara o release.yml
   ```

O `release.yml` recusa a tag se ela não bater com `<Version>` ou se o CHANGELOG não tiver a seção `## [1.1.0]`.
Depois roda `build.ps1 -Lint -Test -Package` e publica, na release:

| Arquivo | O que é |
| --- | --- |
| `V-Clicker.exe`, `V-Clicker-1.1.0-win.zip` | O programa e o zip (exe + licença + READMEs) |
| `*.sha256` | SHA-256 de cada um |
| `*.sigstore.json` | Assinatura [Sigstore](https://www.sigstore.dev/) *keyless* feita pelo `cosign`, vinculada à identidade deste workflow (ninguém guarda chave) |
| `V-Clicker-1.1.0.spdx.json` | SBOM SPDX 2.2 (Microsoft sbom-tool) |
| Atestado de procedência | Registrado no GitHub (aba *Attestations* da release), não é um arquivo |

As notas são a seção do CHANGELOG, uma tabela de como verificar o download e a lista de PRs gerada pelo GitHub.
Para verificar um download:

```powershell
(Get-FileHash V-Clicker.exe).Hash.ToLower() -eq (Get-Content V-Clicker.exe.sha256).Split(' ')[0]
gh attestation verify V-Clicker.exe --repo marquezinii/V-Clicker
cosign verify-blob --bundle V-Clicker.exe.sigstore.json `
  --certificate-identity-regexp '^https://github.com/marquezinii/V-Clicker/\.github/workflows/release\.yml@refs/tags/v' `
  --certificate-oidc-issuer https://token.actions.githubusercontent.com V-Clicker.exe
```

**Opcional**: um segredo `VT_API_KEY` (chave gratuita do [VirusTotal](https://www.virustotal.com/)) faz o release
enviar o `.exe` para análise e pôr o link nas notas. Sem o segredo, o passo é pulado.

## Avisos conhecidos

- O runner do GitHub tem tela de 1024×768 a 100 %: testes de tamanho de janela devem passar um `Rectangle` explícito
  para `FitToScreen` em vez de depender da tela real.
- A pré-release `dev` e os artefatos dos PRs não são assinados nem atestados; só as releases `v*` são.
