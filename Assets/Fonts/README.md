# Fontes

- `BarlowCondensed.ttf`: Barlow Condensed Bold original de `google/fonts/ofl/barlowcondensed`.
- `NunitoSans.ttf`: instância estática normal de peso 600 da variável `NunitoSans[YTLC,opsz,wdth,wght].ttf`, de `google/fonts/ofl/nunitosans`. Eixos restantes fixados nos valores padrão, por fontTools 4.62.1 (`instantiateVariableFont`). Nome de família interna da derivação: Proletariado Sans SemiBold. Conteúdo de licença/copyright preservado.
- Os arquivos OFL ao lado acompanham a redistribuição. Não há dependência de fontes instaladas no Windows.

Para reconstruir, baixe a variável normal do repositório Google Fonts, fixe todos os eixos nos defaults e `wght=600`, e atualize os registros de nome da derivação antes de salvar. A interface usa este TTF via `res://Assets/Fonts/NunitoSans.ttf`.
