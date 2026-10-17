# Levantamento visual: AdVenture Capitalist → Proletariado Simulator

## Alvo e método

Alvo encontrado instalado na Steam: app **346900**, build **7677375**, diretório `steamapps/common/AdVenture Capitalist`. O manifesto indicava instalação concluída (StateFlags 4). Não foi necessário reinstalar o jogo. Arquivos comerciais permanecem somente na instalação original; o material de pesquisa local fica em `artifacts/research/`, fora do Git e da exportação.

Ferramenta: **REA 6.5.0**, instalada com dependências em `artifacts/research/tooling`, usando Node 22.21.0. Nenhuma configuração global de agente foi alterada.

```powershell
npm install --prefix artifacts/research/tooling --no-save --no-audit --no-fund --ignore-scripts rea-agents@6.5.0
$assemblyPath = 'C:\Program Files (x86)\Steam\steamapps\common\AdVenture Capitalist\adventure-capitalist_Data\Managed\Assembly-CSharp.dll'
node artifacts/research/tooling/node_modules/rea-agents/scripts/rea.mjs inspect-managed-artifact $assemblyPath --json
node artifacts/research/tooling/node_modules/rea-agents/scripts/rea.mjs inspect-managed-members $assemblyPath --json
```

## Evidências observadas

- Assembly de 2.139.648 bytes, SHA-256 `cc3937967ad59451444160cba3b33c365dfc6e298edf39d362c77852733327b8`.
- O REA classificou o artefato como PE gerenciado, Unity Mono, implementação CIL e arquitetura gerenciada AnyCPU.
- Metadados: 2.684 tipos, 12.786 métodos, 12.559 campos e 32 referências de assemblies.
- Referências presentes incluem `UnityEngine.UI`, `UnityEngine.UIModule`, `UniRx`, `DOTween` e `DOTweenPro`. A presença dessas bibliotecas não prova o comportamento de um controle específico.
- Evidência do artefato: `ev_0d8f90637f01c40f9b50453a6a45be86eba4a5ffdfb9515af8dac8ff023d2c03`.
- A inspeção de membros, inclusive tentativa filtrada por tipo candidato, foi recusada pelo limite de representação de 64 MiB: `Managed metadata exceeds the 67108864 byte representation budget`. Portanto, não houve rastreamento de controladores, recuperação de código de interface ou extração do layout Unity.
- Evidência de membros: `ev_9245480c310222e9f6a80972d8f06e16fbfe5047744c47a93e061e3e45328232`.

Os JSONs integrais e `summary.json` estão no material local de pesquisa. O parser não executou o assembly alvo.

## Referência visual pública

Foram inspecionadas duas imagens promocionais clássicas da [página oficial na Steam](https://store.steampowered.com/app/346900/AdVenture_Capitalist/):

- [Imagem 0](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/346900/ss_3cb1203117f87367556cd4832f960fb9b1a25994.1920x1080.jpg): estado inicial e atividades ainda não compradas.
- [Imagem 2](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/346900/ss_24fbcd72d11e70c34a041ab3c6e4fe661f111ccd.1920x1080.jpg): estado avançado e compra em lote.

São peças promocionais de 1328×829, com personagem e frases sobrepostos à tela. **Não são capturas desta execução da Steam.** As proporções abaixo são aproximações visuais, não medidas do canvas original.

| Observação nas imagens | Adaptação autoral |
| --- | --- |
| Saldo é o maior elemento tipográfico, na faixa superior | Saldo dominante com caixa de cada mundo e indicadores secundários |
| Retrato e menu ocupam aproximadamente um sexto da largura do quadro | Lateral com Zé do Corre e seis acessos funcionais |
| Atividades em duas colunas, agrupando ícone, quantidade, rendimento, compra e tempo | Oito atividades, quatro linhas; ilustrações próprias e barra de ciclo clicável |
| Compra em laranja, rendimento em verde, contornos escuros e superfícies planas | Tijolo e petróleo, papel creme e amarelo de cartaz; estados explícitos por texto |
| Controles repetidos na mesma posição facilitam comparar atividades | Contrato único de cartão e seletor global 1/10/100/Máximo |
| Personagem exagerado e humor visual | Charge brasileira original e evolução trabalhador/sindicalista/candidato/presidente |

Melhorias, parceiros, marcos, prestígio e campanha são painéis sobrepostos do nosso jogo. Sua implementação e conteúdo são próprios; as capturas públicas não demonstram todas as interações desses menus no original.

## Limitação de observação interativa

A skill Windows computer-use foi inicializada, repetida após espera e reiniciada. Todas as tentativas falharam com `Computer Use native pipe is unavailable: failed to connect native pipe: O sistema não pode encontrar o arquivo especificado. (os error 2)`.

Assim, não foram capturadas nesta sessão as telas do original em execução, nem validados seus cliques, resolução, trocas de mundo ou prestígio. A análise concluída consiste na identificação estática pelo REA e no estudo das referências visuais oficiais. O novo jogo é validado separadamente por seu motor e fluxo gráfico renderizado no Godot.
