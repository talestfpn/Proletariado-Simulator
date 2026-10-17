# Balanceamento e reprodução

O alvo desta versão é a **mediana de 2–4 horas de um jogo novo até a primeira Presidência**, com reinvestimento ativo consistente. O primeiro prestígio de Corre deve ocorrer entre **20–30 minutos**. Derrotas eleitorais podem estender a partida além de quatro horas.

## Parâmetros finais

Os números editáveis ficam em `Data/balance.json`; apresentação, nomes e textos ficam em `Data/theme.json`. As três economias, seus preços, rendimentos e escalas de prestígio foram preservados. O ajuste político escolhido foi somente **`politics.supportEmissionFactor`: 1000 → 2000**.

```text
apoio total emitível = floor(2000 × sqrt(ganhos históricos de Brasília / escala de prestígio))
apoio novo = max(0, apoio total emitível − apoio total já emitido)
```

A escala de Brasília permanece em `7.5e10`. Gastar apoio ou prestigiar não apaga seu histórico de emissão. O bônus econômico de prestígio continua em 25% por ponto, por mundo, e o limite offline continua em oito horas de produtores automatizados.

| Cargo | Duração | Apoio Normal | Verba Normal | Bônus global da vitória |
| --- | ---: | ---: | ---: | ---: |
| Vereador | 10 min | 100 | 1 milhão | +10% |
| Deputado | 12 min | 400 | 100 milhões | +15% |
| Governador | 14 min | 1.200 | 10 bilhões | +20% |
| Presidente | 15 min | 3.000 | 1 trilhão | +30% |

Normal, Reforçado e Tudo no Palanque custam 1×, 2× e 4×, com chances iniciais de 55%, 65% e 75%. Cada escolha pela base soma 10 pontos percentuais à chance; cada escolha por projeção pessoal soma 5 pontos percentuais ao bônus se houver vitória. A chance tem teto de 90%. Os bônus de cargos são somados e o multiplicador resultante multiplica a produção, junto aos bônus econômicos.

## Estratégia usada na medição

Cada semente começa em um estado novo, sem dinheiro injetado, aceleração ou produção offline. O simulador usa passos de 0,25 segundo, inicia atividades manuais ociosas e compra parceiros assim que disponíveis. Em cada mundo, escolhe a unidade ou melhoria com maior produção marginal por custo; se não puder pagar a melhor opção, guarda o saldo.

O primeiro prestígio de Corre e Sindicato é realizado assim que disponível, liberando o mundo seguinte. A simulação não depende de repetir prestígios. Em Brasília, reserva o dinheiro da próxima campanha quando já possui o apoio necessário. Usa sempre investimento **Normal**, escolhe **fortalecer a base** nas duas decisões e tenta novamente imediatamente após uma derrota. Todos os mundos liberados continuam produzindo.

As 100 sementes são os inteiros de **0 a 99**, usando `System.Random` em .NET 8 e uma amostra por abertura de campanha. Depois de cada eleição, o simulador confirma que o estado é válido para salvar. A duração máxima de observação é 12 horas simuladas; todas as 100 partidas concluíram a carreira.

## Resultados

| Medida | Fator inicial 1000 | Fator final 2000 |
| --- | ---: | ---: |
| Partidas concluídas | 100/100 | 100/100 |
| Corre → primeiro prestígio / Sindicato | 25,08 min | 25,08 min |
| Brasília liberada | 50,40 min | 50,40 min |
| Primeira campanha | 56,11 min | 53,95 min |
| Novo jogo → Presidência, mediana | 3,809 h | **3,201 h** |
| Novo jogo → Presidência, P90 | 4,571 h | **4,167 h** |
| Menor duração | 3,477 h | 3,103 h |
| Maior duração | 5,912 h | 5,697 h |

Com o fator final, a mediana da abertura da última campanha é aproximadamente **2,951 h** desde o início; sua conclusão exige os 15 minutos finais da campanha presidencial. O JSON registra por semente o início da última campanha, as tentativas por cargo, o tempo em campanhas e o tempo de espera por recursos após liberar Brasília.

As duas metas foram atingidas: primeiro prestígio em 25,08 minutos e mediana até a Presidência de 3,201 horas. O P90 acima de quatro horas reflete partidas com derrotas e novos investimentos. A referência é uma estratégia de cliques frequentes; o tempo de uma pessoa também depende de suas escolhas e frequência de interação.

A mediana é a média dos dois valores centrais. O P90 é o valor na posição `ceil(0,90 × N)` dos resultados ordenados. Os parâmetros completos, as 100 observações individuais e o resumo são gerados localmente em `artifacts/political-calibration.json` e `.md`. A comparação inicial foi preservada em `artifacts/political-calibration-baseline.json` e `.md`; `artifacts/` não é versionado.

## Como reproduzir

Execute na raiz do projeto, com o SDK .NET 8:

```powershell
# Regras econômicas, política, persistência e integridade dos recursos de tema.
dotnet run --project Tests/Idle.Tests.csproj

# Estratégia original do primeiro prestígio, sem reiniciar a run.
dotnet run --project Tests/Idle.Tests.csproj -- --calibrate

# Carreira completa: 100 sementes desde um jogo novo, com os parâmetros finais.
dotnet run --project Tests/Idle.Tests.csproj -- --campaign-calibrate --runs=100 --output=artifacts/political-calibration.json

# Comparar o fator inicial sem modificar os dados do jogo.
dotnet run --project Tests/Idle.Tests.csproj -- --campaign-calibrate --runs=100 --support-factor=1000 --output=artifacts/political-calibration-baseline.json
```

A suíte tem **133 verificações**, incluindo compras, números grandes, progressão temporal, emissão de apoio, cargos, decisões, limites de chance, sorteio persistido, derrota/cancelamento, bônus no instante da eleição, migração v1→v2, backup, relógio retrocedido e reabertura sem ganhos ou recompensas duplicados. Os recursos de tema são verificados pelos IDs estáveis: 24 SVGs diferentes, 24 parceiros, 72 melhorias e dois eventos para cada cargo.
