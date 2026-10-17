# Arte e identidade

Retratos e cenários originais gerados com a ferramenta integrada `image_gen.imagegen`, usando o primeiro retrato de Zé do Corre como referência para as três evoluções. Os sete prompts exatos e arquivos finais estão em [art-prompts.json](art-prompts.json).

Os PNGs finais ficam em `Assets/Art/`: três fundos `background-world-N.png` e quatro recortes transparentes `portrait-worker.png`, `portrait-union.png`, `portrait-candidate.png` e `portrait-president.png`. Foram copiados para o projeto e inspecionados individualmente.

Os 24 ícones de atividade são desenhos vetoriais próprios em `Assets/Icons/world-N/`, com fonte reproduzível `Assets/Icons/generate_icons.py`. A marca em `Assets/Brand/icon.svg` representa um megafone e usa a mesma paleta.

Paleta: papel `#F4E7C6`, tijolo `#BD3B32`, petróleo `#245A57`, mostarda `#E8AF37` e tinta `#252523`. Interface, números e textos são controles Godot editáveis, sem texto embutido nas ilustrações.

Fontes: **Nunito Sans** e **Barlow Condensed Bold**, distribuídas pelo repositório Google Fonts. O arquivo `NunitoSans.ttf` é uma instância estática normal de peso 600, gerada com fontTools 4.62.1 a partir da variável oficial e identificada internamente como **Proletariado Sans SemiBold** para distinguir a derivação. Os demais eixos foram fixados nos valores padrão; o itálico não é usado. Os arquivos de licença OFL acompanham os TTFs em `Assets/Fonts/`. Nenhuma biblioteca externa de ícones é necessária.

Para substituir a arte, preserve IDs em `Data/theme.json` e altere `iconPath`, `backgroundPath` ou as entradas `portraits`. O save utiliza IDs e números, não os nomes nem os caminhos das imagens.
