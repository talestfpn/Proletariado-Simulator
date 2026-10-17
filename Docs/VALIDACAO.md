# Validação da entrega

- Build integrado `dotnet build IdleLab.csproj --nologo`: passou, zero avisos e erros.
- Suíte de regras, política, persistência e tema: **133/133** verificações aprovadas após o ajuste final de dados.
- Fluxo gráfico renderizado: passou em **1280×800 e 1100×700**, com oito cartões visíveis; compras, melhorias, parceiros, marcos, prestígio, tema, campanha, decisões, cancelamento, derrota, quatro cargos e Presidência.
- Log gráfico: `artifacts/ui-retro-flow.log`; stderr vazio. Capturas `artifacts/ui-*.png`.
- Teste de abertura no editor/headless: `UI_READY worlds=3 producers=8 retro_politics=ready` e `UI_SMOKE_OK`, saída 0.
- Exportação release Windows: saída 0, sem ERROR/WARNING no log `artifacts/export-final.log`.
- Teste do **executável exportado**, com `--headless -- --smoke-test`: mesmos marcadores, saída 0 e stderr vazio. Save de smoke isolado do jogador.
- ZIP conferido: **194 entradas**, incluindo o executável, bibliotecas .NET, instruções e licenças das fontes. Tamanho: **88.239.449 bytes**.
- SHA-256 do executável: `723ca24955e44254e3ef567d132c5cec0efe4a05807767263ff750c88a14ef90`.
- Calibração: **100/100 sementes**, mediana **3,200625 h**, P90 **4,16652778 h**; primeiro prestígio **25,0791667 min**. Método e números em [BALANCEAMENTO.md](BALANCEAMENTO.md).

## Abrir a entrega local

`artifacts/windows/Proletariado Simulator.exe` precisa da pasta `data_IdleLab_windows_x86_64` ao lado. Para transportar a distribuição inteira, use `artifacts/Proletariado-Simulator-Windows.zip` e extraia todo seu conteúdo.

O fluxo gráfico exercita sinais dos controles e o estado real da aplicação, com renderização Godot. Ele não substitui teste manual de mouse/teclado por uma pessoa. A observação interativa do AdVenture Capitalist original ficou indisponível; os detalhes e evidências do REA estão em [ANALISE-VISUAL.md](ANALISE-VISUAL.md).
