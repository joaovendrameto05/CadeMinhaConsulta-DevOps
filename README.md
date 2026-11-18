# Cadê Minha Consulta .NET

API ASP.NET Core Minimal API com alvo `net10.0`. A arquitetura prevê que o Mobile React Native acesse Java e .NET diretamente. Java é responsável por usuários, pacientes, consultas, confirmações, cancelamentos, fila e realocação no Oracle DBv3. A API .NET lê o Oracle por EF Core e usa MongoDB para seus próprios dados. A validação JWT é local.

Consulte a [arquitetura](docs/Arquitetura.md), a [configuração Oracle DBv3](docs/Oracle-DBv3.md), as [evidências de validação](docs/Validacao-DotNet.md) e o [roteiro para a banca](docs/Roteiro-Demonstracao.md).

## Executar localmente

Use o SDK .NET 10 ou um SDK posterior compatível e o runtime ASP.NET Core 10.

No Windows, baixe o MongoDB Community 8.0.38 portátil e confira o SHA-256 do ZIP. Extraia `mongod.exe` em `.local/mongodb/mongodb-win32-x86_64-windows-8.0.38/bin/` ou passe seu caminho em `-MongoExecutable`. O script reutiliza `.local/mongodb/data` e abre a porta 27017 apenas em `127.0.0.1`. Depois de entrar novamente no Windows, inicie o processo pelo script. A opção `-Foreground` permite encerrar com Ctrl+C. A inicialização em segundo plano espera a abertura da porta e recusa iniciar se ela já estiver ocupada. Use essa instância exclusiva de loopback para dados de desenvolvimento. [ZIP oficial](https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-8.0.38.zip) · [SHA-256 oficial](https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-8.0.38.zip.sha256)

```powershell
.\tools\Start-LocalMongo.ps1 -Foreground
```

No ambiente validado, a conexão local e o banco `vagacerta_dev` já estão em User Secrets. Nas demais máquinas, configure `ConnectionStrings:Mongo` e `Mongo:Database` em **Gerenciar Segredos do Usuário** no projeto `CadeMinhaConsulta.Api`. Para o Oracle, configure `ConnectionStrings:Oracle` e `Oracle:Schema` no mesmo local quando houver um DBv3 de teste disponível. Use uma conta somente leitura e mantenha os valores fora do repositório.

### Visual Studio

1. Abra `CadeMinhaConsulta.slnx` no Visual Studio com o workload de desenvolvimento web ASP.NET e selecione `CadeMinhaConsulta.Api` como projeto de inicialização.
2. No PowerShell 7, gere os tokens locais de teste com `$tokens = & .\tools\New-DevelopmentJwt.ps1 -SavePublicSettings`. Essa opção atualiza as configurações locais de validação JWT para o emissor de demonstração e grava apenas a parte pública em User Secrets. Copie o token desejado com `$tokens.Gestor | Set-Clipboard` ou `$tokens.Cidadao | Set-Clipboard`.
3. Execute o perfil **CadeMinhaConsulta.Api** com F5 ou Ctrl+F5. O perfil abre o Swagger em `http://localhost:5050`. Clique em **Authorize** e cole o token. Regenere os tokens após 30 minutos.

### Terminal

```powershell
dotnet restore CadeMinhaConsulta.slnx
dotnet build CadeMinhaConsulta.slnx --no-restore
dotnet test CadeMinhaConsulta.slnx --no-build --no-restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/CadeMinhaConsulta.Api -- --urls http://localhost:5050
```

Use HTTP apenas para rodar localmente no loopback. Use HTTPS no ambiente acessado pelo Mobile. Configure as variáveis de ambiente abaixo ou use User Secrets no projeto da API em desenvolvimento.

| Variável | Valor esperado |
|---|---|
| `ConnectionStrings__Oracle` | Conexão Oracle com uma conta dedicada. A conta precisa de permissão de sessão e de leitura sobre `TB_GS_CONSULTA` no schema proprietário. |
| `Oracle__Schema` | Nome do schema proprietário do DBv3, conforme cadastrado no Oracle. |
| `ConnectionStrings__Mongo` | URI do MongoDB do ambiente. |
| `Mongo__Database` | Banco reservado para os dados .NET do Cadê Minha Consulta. |
| `Jwt__Issuer` | Emissor acordado com Java. |
| `Jwt__Audience` | Audiência .NET acordada com Java. |
| `Jwt__PublicKeyPem` | Conteúdo PEM da chave pública RSA. Em PowerShell, use `Get-Content -Raw` para ler o arquivo da chave pública. |
| `Jwt__OracleUserIdClaim` | Nome da claim assinada que contém o `ID_USUARIO` numérico. Configure `sub` somente se a equipe Java confirmar esse vínculo. |
| `Jwt__RoleClaim` | Nome da claim assinada com os perfis. O padrão da API é `role`. Confirme o nome e os valores com a equipe Java para usar tokens reais. |
| `RateLimiting__PermitLimit` | Limite por `sub` autenticado, por janela de um minuto e por instância. Padrão de 60. |

## Verificar a infraestrutura

| Requisição | Resultado esperado |
|---|---|
| `GET /health/live` | HTTP 200 quando o processo atende requisições. |
| `GET /health/ready` | HTTP 200 exclusivamente quando todos os checks obrigatórios estão saudáveis. Dependência degradada, falha ou configuração incompleta retornam HTTP 503. O JSON indica o estado separado de Oracle e MongoDB. |
| `GET /api/diagnostics/auth` | Disponível em `Development`. HTTP 204 com token válido e `sub` presente. HTTP 401 para token ausente ou inválido, 403 para token válido sem `sub` e 429 após exceder o limite. |
| `GET /swagger` | Interface Swagger em `Development`. O documento está em `/swagger/v1/swagger.json`. |

O check Oracle faz um SELECT de até uma linha e omite os dados da resposta. Para validar a leitura real, use um DBv3 de teste já provisionado com ao menos uma consulta fictícia. Confirme que a conta .NET possui apenas as permissões de leitura necessárias e que o schema e as colunas correspondem ao mapeamento. O check MongoDB verifica conectividade por ping. Para validar a escrita na coleção, execute o teste pelo Swagger com uma consulta fictícia elegível.

O validador inicial aceita RS256 com chave pública configurada, `iss`, `aud` e expiração. Confirme o algoritmo e as claims com a equipe Java e teste um token emitido por ela. Antes de liberar acesso a dados, valide a correspondência entre a identidade autenticada e `ID_USUARIO`, a faixa dos IDs e o fuso de `DATA_HORA_CONSULTA`.

O DBv3 foi implantado no Oracle FIAP e validado com 13 tabelas ativas. O Kestrel retornou HTTP 200 em `/health/live` e `/health/ready`, com Oracle e MongoDB saudáveis. No fluxo real, as consultas de dois titulares fictícios foram conferidas contra SELECTs no Oracle. O MongoDB persistiu as notificações e o estado de leitura. Veja o [relatório de validação](docs/Validacao-DotNet.md) e o [registro da implantação](docs/Oracle-DBv3.md).

## Notificações internas

| Requisição | Comportamento |
|---|---|
| `GET /api/notifications/me` | Lê apenas as consultas do titular no Oracle e retorna HTTP 200 com as notificações desse usuário, inclusive as já observadas. Sem notificações, retorna `[]`. |
| `PATCH /api/notifications/{id}/read` | Define `isRead` como `true`. Retorna HTTP 204 mesmo se a notificação já estiver lida. Um ID ausente ou pertencente a outro titular recebe o mesmo HTTP 404. |

Ambas as rotas retornam HTTP 401 para token ausente ou inválido, HTTP 403 se a associação com o titular não puder ser determinada, HTTP 429 ao exceder o limite e HTTP 503 em falhas de dependência. O PATCH recebe apenas o ID na rota e usa exclusivamente o MongoDB.

As notificações são atualizadas **mediante consulta do Mobile**. Para cada combinação de usuário, consulta e status observado (`AGENDADA`, `CONFIRMADA` ou `CANCELADA`), a API mantém um documento na coleção `notifications` do banco configurado em `Mongo__Database`. O `_id` é o SHA-256 dessa combinação e o índice único `_id_` é criado automaticamente pelo MongoDB. O upsert usa `$setOnInsert`, preservando o primeiro instante observado e o estado de leitura em consultas repetidas. `observedAtUtc` é o instante em que a API observou aquele estado pela primeira vez. Estados intermediários podem passar sem registro entre duas consultas. `appointmentAt` preserva o horário do `DATE` Oracle, sem atribuir offset antes de validar o fuso.

### Testar pelo Swagger

1. Configure os bancos de teste e os dados JWT acima. Use dois titulares com consultas fictícias `AGENDADA`, `CONFIRMADA` ou `CANCELADA`. Para a demonstração local, o povoamento validado contém os IDs de teste 2 e 3. Gere ambos os tokens na mesma sessão PowerShell com `$tokens = & .\tools\New-DevelopmentJwt.ps1 -TestPatientOracleId 2 -SecondTestPatientOracleId 3`.
2. Inicie a API em `Development` conforme os comandos acima. Para testar a geração com Oracle real, confirme HTTP 200 no [readiness local](http://localhost:5050/health/ready). Com Oracle indisponível, o GET retorna 503. A marcação de leitura continua disponível para documentos que já existam no MongoDB.
3. Abra o [Swagger local](http://localhost:5050/swagger). Clique em **Authorize** e cole o token do primeiro titular. Em desenvolvimento local, use `$tokens.Cidadao`. Para a integração com Java, use um JWT com o emissor, a audiência e as claims configurados. Confirme HTTP 204 no diagnóstico antes de consultar dados.
4. Execute `GET /api/notifications/me` duas vezes e confira o mesmo ID para aquela consulta e status. Copie o ID e execute `PATCH /api/notifications/{id}/read` duas vezes. Ambos devem retornar 204 e o próximo GET deve trazer `isRead: true`.
5. Autorize o segundo usuário fictício. Na demonstração local, use `$tokens.OutroCidadao`. O GET deve listar apenas o que pertence a ele. Um PATCH com o ID do primeiro usuário deve retornar 404.

Exemplo fictício de GET:

```json
[
  {
    "id": "6d04f1e33113c473054da98807772f52d9e1f61d8f586dd5672ac5434770e453",
    "appointmentId": "88",
    "status": "AGENDADA",
    "appointmentAt": "2026-12-01T09:30:00",
    "observedAtUtc": "2026-11-17T12:00:00Z",
    "isRead": false
  }
]
```

## Regras de lembrete

O CRUD usa exclusivamente o MongoDB. Com o MongoDB configurado, o CRUD funciona enquanto o Oracle estiver indisponível. O readiness geral verifica os dois bancos e, nesse caso, retorna 503.

| Requisição | Perfis | Sucesso |
|---|---|---|
| `GET /api/reminder-rules` | CIDADAO ou GESTOR | `200` com a lista. |
| `GET /api/reminder-rules/{id}` | CIDADAO ou GESTOR | `200` com a regra. |
| `POST /api/reminder-rules` | GESTOR | `201` com a regra original e o cabeçalho `Location`. |
| `PUT /api/reminder-rules/{id}` | GESTOR | `200` com a regra atualizada. |
| `DELETE /api/reminder-rules/{id}` | GESTOR | `204`. |

POST e PUT exigem `name` com 1 a 100 caracteres, `daysBefore` inteiro de 0 a 365, `message` com 1 a 500 caracteres e `isActive` booleano. A API remove espaços nas extremidades do nome e da mensagem. As respostas incluem `createdAtUtc` e `updatedAtUtc`. Erros de entrada retornam 400, credenciais ausentes ou inválidas retornam 401, perfis insuficientes retornam 403, e regras ausentes ou excluídas retornam 404.

### Idempotência do POST

Envie um único cabeçalho `Idempotency-Key` com 1 a 128 caracteres ASCII. São aceitos letras, dígitos, ponto, hífen e sublinhado. A chave é sensível a maiúsculas e minúsculas. Use uma chave diferente para cada nova intenção de criação.

O MongoDB mantém um documento em `reminder_rules` por combinação de emissor, `sub`, operação e chave. O hash dessa combinação é o `_id`, protegido pelo índice único `_id_`. Uma única inserção atômica grava a regra e a cópia da criação original, com confirmação por maioria e journal. A chave em texto e o identificador do usuário ficam fora do documento. Uma repetição com os mesmos campos validados devolve o mesmo `201`, `Location` e corpo inicial. Um conteúdo válido diferente retorna `409`. Um corpo inválido retorna `400` antes de consumir a chave.

O PUT altera apenas a versão atual. O DELETE retira a regra das consultas e mantém o documento com a data de exclusão. A cópia original e a chave permanecem disponíveis para replay mesmo após edição, exclusão ou reinício da API. Não há expiração automática. Se uma falha deixar a confirmação da gravação indeterminada, a API retorna `503` e o cliente deve repetir com a mesma chave e corpo.

### Demonstrar pelo Swagger sem depender do Java

1. Configure a conexão e o nome de um banco MongoDB de teste, conforme a tabela de variáveis. No **PowerShell 7**, rode:

   ```powershell
   $tokens = & .\tools\New-DevelopmentJwt.ps1
   dotnet run --project src/CadeMinhaConsulta.Api -- --urls http://localhost:5050
   ```

   O script cria uma chave RSA efêmera e tokens de 30 minutos para GESTOR e CIDADAO. A API rejeita o emissor de demonstração fora de `Development`. Para copiar um token na mesma sessão do PowerShell, use `$tokens.Gestor | Set-Clipboard` ou `$tokens.Cidadao | Set-Clipboard`. Se a API estiver rodando em primeiro plano, copie o token antes de iniciá-la ou use um servidor iniciado em background com a mesma configuração.
2. Abra o [Swagger](http://localhost:5050/swagger). Sem autorizar, execute o GET e confira 401. Clique em **Authorize**, cole o token CIDADAO e confira que o GET retorna 200 e o POST retorna 403.
3. Troque para o token GESTOR. Execute o POST com a chave `demo-regra-1` e o corpo abaixo. Use o ID ou o `Location` para testar GET, PUT e DELETE. No PUT, envie o corpo completo com os campos editáveis.

   ```json
   {
     "name": "Lembrete de consulta",
     "daysBefore": 2,
     "message": "Confira o horário da sua consulta no aplicativo.",
     "isActive": true
   }
   ```

4. Repita o POST com a mesma chave e corpo. Confira o mesmo `201`, `Location` e corpo. Mude `daysBefore`, mantenha a chave e confira `409`. Use uma chave nova se a demonstração já tiver sido executada nesse banco com o mesmo usuário.
5. Para demonstrar o limite, reinicie a API com `$env:RateLimiting__PermitLimit = '2'`. Com o mesmo JWT, faça três GETs dentro de um minuto. O terceiro retorna `429` com `Retry-After`. Um token com outro `sub` tem sua própria cota. Sem token o endpoint responde `401` antes do rate limiter por usuário.

### Teste com MongoDB real

Configure `VAGACERTA_TEST_MONGO_URI` e `VAGACERTA_TEST_MONGO_DATABASE` com um banco dedicado cujo nome comece com `vagacerta_test_`. Os testes usam o repositório MongoDB real, verificam os índices `_id_` e recriam o host da API. O teste de regras verifica concorrência, replay e CRUD. O de notificações usa consultas fictícias geradas pela suíte e verifica deduplicação, titularidade e persistência da leitura. Cada teste exclui apenas seu próprio documento ao final.

```powershell
$env:VAGACERTA_TEST_MONGO_URI = 'mongodb://127.0.0.1:27017'
$env:VAGACERTA_TEST_MONGO_DATABASE = 'vagacerta_test_local'
dotnet test CadeMinhaConsulta.slnx --filter 'Dependency=MongoDB'
```

Para incluir os dois cenários na suíte inteira, mantenha as variáveis configuradas e execute `dotnet test CadeMinhaConsulta.slnx`. Sem elas, esses dois testes são pulados. Os demais cenários cobrem HTTP em memória, JWT assinado para testes, BSON e SQL Oracle gerado.

## Consumo pelo React Native

O [contrato Mobile](docs/Contrato-Mobile.md) documenta a URL base por ambiente, autenticação e a tela de exemplo para listar regras. O backend respondeu à chamada HTTP de verificação. A execução e a captura no React Native dependem do projeto mantido pela equipe Mobile. O Swagger descreve todas as rotas disponíveis no perfil local, incluindo os endpoints públicos de saúde.

## Entrega

O [checklist de validação](docs/Validacao-DotNet.md) registra as evidências e pendências, e o [roteiro para a banca](docs/Roteiro-Demonstracao.md) relaciona cada critério aos arquivos e testes. A entrega .NET é um ZIP com o link real do repositório GitHub com README e o TXT de identificação dos cinco integrantes. Inclua o PowerPoint se houver. O ZIP preliminar e o TXT ficam em `.local/entrega`, fora do Git. Preencha o link verdadeiro antes do envio.

