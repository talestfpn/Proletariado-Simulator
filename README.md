# Proletariado Simulator

**Do ponto batido ao palanque.** Um idle satírico brasileiro em **Godot 4.7.1 .NET / C# / .NET 8**. Zé do Corre começa nos bicos, organiza a base e chega à disputa pelo poder. Arte de cartaz retrô, três mundos com oito atividades e carreira política até a Presidência.

## Jogar

- **Windows:** abra `artifacts/windows/Proletariado Simulator.exe`. Mantenha a pasta de dados que acompanha o executável. A exportação inclui o runtime .NET. Para transportar a distribuição, extraia todo o conteúdo de `artifacts/Proletariado-Simulator-Windows.zip`.
- **Godot:** importe `project.godot` na edição **.NET**, compile e pressione F5.
- **PowerShell:** execute `./run.ps1`. Para indicar o editor, use `./run.ps1 -GodotPath 'C:\caminho\Godot_mono.exe'`.

O jogo é local, sem conta, monetização ou serviços online. O humor inclui palavrões e sátira política com personagens fictícios.

## Do corre à Presidência

1. Clique na barra da primeira atividade para iniciar um ciclo. A conclusão paga recursos.
2. Reinvista com **1, 10, 100 ou Máximo**. Lotes fixos exigem o valor inteiro; Máximo calcula a quantidade que cabe no saldo.
3. Contrate **Parceiros** para automatizar atividades, inclusive nos mundos não selecionados e durante a ausência.
4. Compre melhorias e alcance marcos de quantidade. As melhorias mostram nomes, piadas e efeitos numéricos.
5. Faça **Prestígio** com recompensa positiva. O primeiro prestígio de Corre libera Sindicato; o primeiro de Sindicato libera Brasília.
6. Em Brasília, acumule **Apoio Político**, invista em campanhas e dispute Vereador, Deputado, Governador e Presidente.

Os caixas **Grana do Corre, Fundo Sindical e Verba de Brasília** são independentes. Comprar atividades representa ampliar frentes de produção; os três mundos avançam juntos.

## Regras econômicas

Os preços seguem `baseCost × costGrowth^quantidade`. O custo do lote é a soma geométrica dos preços individuais. A unidade gratuita já conta no índice do preço.
O pagamento combina quantidade, rendimento base, melhorias, marcos, prestígio e bônus político global. Alterações de velocidade preservam a proporção concluída do ciclo. A produção por segundo contabiliza atividades automatizadas.

```text
pontos disponíveis = max(0, floor(sqrt(ganhos brutos históricos / escala)) − pontos concedidos)
multiplicador do mundo = 1 + pontos concedidos × 0,25
```

Os ganhos históricos incluem recursos gastos. Prestigiar zera saldo, atividades, melhorias, parceiros e ciclos somente daquele mundo, devolvendo a unidade inicial. Preserva histórico, pontos, política e mundos liberados. O primeiro prestígio do Corre foi calibrado para cerca de 25 minutos de reinvestimento ativo.
Valores monetários usam `BigValue`, mantissa e expoente com aproximadamente 15–16 dígitos significativos. Nos JSONs, use strings como `"7.5e7"` ou `"1e400"`.

## Política

A carreira abre com Brasília. O apoio é emitido pela diferença entre o total calculado a partir dos ganhos históricos e o total já emitido; gastar apoio ou prestigiar não emite novamente o mesmo recurso.

```text
apoio total emitível = floor(2000 × sqrt(ganhos históricos de Brasília / escala de prestígio))
```

| Cargo | Duração | Apoio básico | Verba básica | Bônus global |
| --- | --- | --- | --- | --- |
| Vereador | 10 min | 100 | 1 milhão | +10% |
| Deputado | 12 min | 400 | 100 milhões | +15% |
| Governador | 14 min | 1.200 | 10 bilhões | +20% |
| Presidente | 15 min | 3.000 | 1 trilhão | +30% |

**Normal, Reforçado e Tudo no Palanque** investem 1×/2×/4× de dinheiro e apoio, com chance inicial de 55%/65%/75%. O pagamento acontece no início.
Em ⅓ e ⅔ da campanha, uma decisão pausa seu relógio. Fortalecer a base adiciona **10 pontos percentuais de chance**; priorizar projeção pessoal adiciona **5 pontos percentuais de bônus permanente se houver vitória**. A chance é limitada a 90%.
O sorteio é salvo ao iniciar e comparado à chance final na votação. Derrota ou cancelamento perde somente o investimento. Cada cargo pode ser conquistado uma vez, em sequência; cargos anteriores permanecem. Os bônus políticos se somam, formando um multiplicador global. A Presidência conclui a história e permite continuar o idle.
O prestígio de Brasília aguarda terminar ou cancelar uma campanha ativa. Sua produção e suas compras continuam funcionando.

## Save e retorno

O save **v2** migra o formato v1 automaticamente. Um save antigo recebe apoio correspondente ao histórico de Brasília, sem cargos ou bônus retroativos.
O diretório `user://` foi fixado em `%APPDATA%\Godot\app_userdata\Idle Lab\` para preservar o progresso, mesmo com o novo nome. Há `save.json` e `save.backup.json`, escrita temporária, flush e substituição atômica. Salva a cada 30 segundos, após ações importantes e ao fechar; falhas de gravação são informadas.
O retorno concede até **oito horas** somente às atividades automatizadas, uma única vez. A campanha avança até a primeira decisão pendente; o restante daquele intervalo não fica acumulado para a próxima escolha. Depois da segunda decisão, a votação pode acontecer offline. Ganhos e bônus são aplicados na ordem temporal correta.
Relógio retrocedido não avança o checkpoint para trás. Saves locais não incluem proteção antitrapaça nem coordenação de múltiplas instâncias.

## Alterar tema e balanceamento

- `Data/theme.json`: marca, protagonista, mundos, ícones, retratos, cenários, atividades, parceiros, melhorias e eventos. Preserve os IDs.
- `Data/balance.json`: economia, custos, ciclos, melhorias, marcos e política (`supportEmissionFactor`, cargos, custos, durações e bônus).
- `Assets/`: ilustrações, ícones SVG, marca e fontes com licenças OFL.

Nomes e imagens não participam dos cálculos nem identificam o save. Os IDs de produtores se repetem entre mundos: a identidade completa é mundo + produtor + melhoria.

[Arte e prompts](Docs/ARTE.md) · [Análise do original com REA e limitações](Docs/ANALISE-VISUAL.md) · [Calibração](Docs/BALANCEAMENTO.md) · [Validação da entrega](Docs/VALIDACAO.md)

## Desenvolver e validar

```powershell
dotnet build IdleLab.csproj
dotnet run --project Tests/Idle.Tests.csproj
dotnet run --project Tests/Idle.Tests.csproj -- --calibrate
dotnet run --project Tests/Idle.Tests.csproj -- --campaign-calibrate --runs=100
./run.ps1 -SmokeTest
./run.ps1 -UiFlowTest
```

`Core/` contém as regras sem Godot; `Persistence/` trata snapshots e retorno; `Scripts/` e `Scenes/` apresentam o jogo. A assembly técnica continua `IdleLab` para evitar uma renomeação desnecessária dos projetos C#.
Validação: **133/133 verificações aprovadas**, build sem avisos ou erros, teste de abertura e fluxo gráfico completo em **1280×800 e 1100×700**. O fluxo usa saves de QA isolados, renderiza telas reais e aciona os controles do jogo; as capturas ficam em `artifacts/`, fora do Git.

Calibração final: **100/100 sementes concluídas**, mediana de **3,20 horas** do jogo novo à Presidência e percentil 90 de **4,17 horas**. Primeiro prestígio em **25,08 minutos** e Brasília em **50,40 minutos**. Estratégia: reinvestimento consistente, campanhas Normal e fortalecimento da base. O fator de emissão de apoio foi ajustado de 1000 para **2000**, preservando os custos, durações e a economia dos três mundos. Derrotas podem levar partidas além de quatro horas; método e resultados estão no relatório de calibração.

Exportação com os templates .NET 4.7.1 instalados:

```powershell
& 'C:\caminho\Godot_mono.exe' --headless --path . --export-release 'Windows Desktop' 'artifacts/windows/Proletariado Simulator.exe'
```
