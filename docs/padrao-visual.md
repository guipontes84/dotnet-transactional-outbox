# Padrão visual dos materiais

- Fundo azul muito escuro, texto branco e destaques em ciano/neon.
- Comparações lado a lado, vermelho para falha e verde/ciano para recuperação.
- Diagramas com a topologia explícita e etapas numeradas.
- Assinatura no rodapé: foto de Guilherme Pontes, LinkedIn e repositório do assunto.
- LinkedIn: https://www.linkedin.com/in/guilhermepontesdev
- Repositório deste material: https://github.com/guipontes84/dotnet-transactional-outbox
- A foto está incorporada no SVG, sem depender de download externo.

## Arquivos

`media/outbox-pix.svg` é o original animado. `media/outbox-pix.gif` é a exportação de seis etapas com duração total de 18 segundos, para publicação em redes sociais.

Para exportar novamente, com Python e Edge instalados:

```powershell
python -m pip install pillow playwright
python scripts/export_gif.py
```

O GIF mantém os textos dos endereços, mas não oferece links clicáveis. Coloque o endereço do repositório também no texto da publicação.
