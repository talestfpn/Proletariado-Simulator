# Idle Lab

Base idle para Windows em **Godot 4.7.1 .NET / C# / .NET 8**. São três mundos independentes com oito produtores cada, produção por ciclos, automação, melhorias, marcos e prestígio. A apresentação usa placeholders neutros e pode ser substituída sem alterar as regras.

## Jogar

- **Versão Windows:** abra `artifacts/windows/IdleLab.exe`. Mantenha a pasta `data_IdleLab_windows_x86_64` ao lado do executável, caso seja criada pela exportação. A distribuição inclui o runtime .NET.
- **Editor:** importe `project.godot` no Godot **.NET**, compile pelo botão de build e pressione **F6** em `Scenes/Main.tscn` ou **F5** para executar o projeto.
- **PowerShell:** execute `./run.ps1`. O script procura o Godot .NET no PATH, em `GODOT4` e nas pastas Godot .NET de Downloads. Também aceita `./run.ps1 -GodotPath 'C:\caminho\Godot_mono.exe'`.

Para desenvolver, instale o SDK .NET 8 x64 e use a edição .NET do Godot. A primeira compilação restaura pacotes NuGet. O jogo e os saves funcionam localmente, sem serviços online.

## Como jogar

1. Clique em **Iniciar** para executar um ciclo do primeiro produtor, recebido gratuitamente.
2. Reinvista nos botões de compra. O seletor oferece **1, 10, 100 e Máximo**; lotes fixos exigem o valor inteiro disponível.
3. Compre **Auto** para repetir os ciclos, inclusive quando outro mundo estiver selecionado ou o jogo estiver fechado.
4. Clique no nome de um produtor para selecionar suas melhorias no painel direito. Os marcos de 25, 50, 100, 200, 400 e 800 unidades dobram cumulativamente seu rendimento.
5. Acumule ganhos brutos para habilitar o **prestígio**. A janela de confirmação mostra o que será perdido e mantido.

O primeiro prestígio de um mundo libera o seguinte. Os mundos anteriores continuam produzindo. A produção por segundo exibida no topo considera apenas os produtores automatizados; produtores manuais pagam ao completar o ciclo iniciado.

## Economia e prestígio

`Data/balance.json` contém os parâmetros, separados da apresentação:

| Campo | Função |
| --- | --- |
| `offlineLimitHours` | Limite de produção offline; padrão 8 horas |
| `prestigeBonusPerPoint` | Bônus aditivo de produção por ponto; padrão 0,25 |
| `worlds[].prestigeScale` | Escala de ganhos para o prestígio daquele mundo |
| `baseCost`, `costGrowth` | Custo inicial e crescimento geométrico do preço |
| `basePayout`, `cycleSeconds` | Rendimento por unidade/ciclo e duração base |
| `automationCost` | Preço para repetir os ciclos automaticamente |
| `upgrades`, `milestones` | Melhorias compráveis e bônus por quantidade |

Valores monetários usam **strings com ponto decimal**, por exemplo `"4"`, `"7.5e7"` ou `"1e400"`. O tipo `BigValue` guarda mantissa e expoente, com precisão aproximada de 15–16 dígitos significativos; não usa inteiros de precisão arbitrária.

O próximo preço de um produtor é `baseCost × costGrowth^quantidade`. O lote soma a sequência geométrica, sem arredondamentos intermediários. A unidade gratuita já conta na quantidade. Os pagamentos combinam quantidade, rendimento base, melhorias, marcos e multiplicador de prestígio.

```text
pontos disponíveis = max(0, floor(sqrt(ganhos brutos acumulados / escala)) − pontos concedidos)
multiplicador permanente = 1 + pontos concedidos × bônus por ponto
```

Compras não reduzem os ganhos brutos acumulados. Prestígio exige pontos positivos e zera apenas o saldo, as quantidades, melhorias, automações e ciclos do mundo reiniciado, restaurando a unidade gratuita. Mantém pontos, histórico bruto e mundos liberados. Os outros mundos permanecem intactos.

A primeira run do Setor 01 foi calibrada para **25,079 minutos** até o primeiro ponto. A simulação reinicia produtores ociosos a cada 0,25 s, compra automações quando disponíveis e escolhe a unidade ou melhoria com maior produção marginal por custo, aguardando saldo quando necessário. O tempo de uma pessoa varia conforme suas decisões. Os Setores 02 e 03 usam outros perfis de custos e ciclos e ainda podem ser rebalanceados.

## Trocar o tema

Edite `Data/theme.json`: título, subtítulo, nomes dos mundos, moedas, prestígio, produtores, descrições, glifos e cores. **Preserve os IDs.** Eles conectam apresentação, regras e save; nomes exibidos não são usados pelos cálculos.

Os ícones atuais são formas e glifos. Para usar imagens, adicione PNGs ou SVGs à pasta `Assets/` e preencha o campo opcional `iconPath` do mundo ou produtor no tema, por exemplo `"res://Assets/modulo.png"`. O Godot importa os recursos; a próxima exportação os inclui. Sem `iconPath`, permanece o placeholder. Cenas e estilos podem ser alterados sem mexer em `Core/`.

As cores dos mundos ficam no tema; as cores de superfície e os textos genéricos dos controles ficam na interface. Trocar nomes, glifos e cores preserva o save. Alterar IDs, remover produtores ou mudar a estrutura do estado exige uma migração explícita.

## Saves

O Godot grava em `user://`; no Windows, normalmente `%APPDATA%\Godot\app_userdata\Idle Lab\`:

- `save.json`: snapshot versionado com estado e instante UTC.
- `save.backup.json`: snapshot válido anterior, usado se o principal estiver corrompido.

O save usa arquivo temporário, flush e substituição atômica. Salva a cada 30 segundos, após ações de compra/prestígio e ao fechar. Falha ao salvar é informada; o fechamento é interrompido para permitir nova tentativa. Se ambos os arquivos estiverem inválidos, o jogo os preserva e mostra o erro. Saves de versão futura também são preservados.

Ao abrir, aplica uma única vez até 8 horas aos produtores automatizados e grava o resultado antes de exibi-lo. Ciclos manuais ficam congelados durante a ausência. Se o relógio retroceder, o ganho offline é zero. O relógio é local; esta base não inclui proteção contra manipulação de horário ou múltiplas instâncias simultâneas.

## Estrutura e verificações

- `Core/`: biblioteca C# pura, definições, estado, números grandes e motor de simulação.
- `Persistence/`: snapshots JSON e retorno offline, sem dependência do Godot.
- `Scripts/` e `Scenes/`: interface, eventos e integração com o motor.
- `Data/`: balanceamento e apresentação.
- `Tests/`: verificações determinísticas sem dependências de frameworks externos.

```powershell
dotnet build IdleLab.csproj
dotnet run --project Tests/Idle.Tests.csproj
dotnet run --project Tests/Idle.Tests.csproj -- --calibrate
./run.ps1 -SmokeTest
./run.ps1 -UiFlowTest
```

O runner passou **73 verificações** de lotes, Máximo, ciclos, melhorias, marcos, tempo, mundos em paralelo, prestígio, números grandes, retematização, save, backup e offline. O smoke carrega a interface em uma pasta de save isolada e encerra após três segundos.

`-UiFlowTest` executa o fluxo gráfico em um save de QA isolado: aciona os sinais dos botões, verifica alterações de estado e rótulos, cancela/confirma prestígio e testa uma troca de tema com textura. Usa renderização real em 1280×800 e 1100×700 e grava PNGs em `artifacts/`. A revisão visual usa essas capturas; não depende de automação do mouse do Windows. As fixtures de QA só funcionam no editor/build de debug.

Para exportar, use o preset **Windows Desktop** no editor, com os templates .NET 4.7.1 instalados. O preset inclui os arquivos JSON necessários. Pela CLI:

```powershell
& 'C:\caminho\Godot_mono.exe' --headless --path . --export-release 'Windows Desktop' 'artifacts/windows/IdleLab.exe'
```

Esta versão usa progressão permanente automática. Árvore de habilidades, redistribuição de pontos, arte definitiva, áudio, monetização, eventos e serviços online ficam para etapas posteriores.
