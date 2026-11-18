# Roteiro técnico para a banca .NET

O Cadê Minha Consulta ajuda a reduzir a incerteza sobre consultas e horários por meio de notificações internas. A API .NET consulta o DBv3 com filtro por titular e mantém o histórico de observação e leitura no MongoDB. A equipe pode mostrar o CRUD real de regras e o consumo dessas regras no aplicativo.

## Critérios das páginas 23–25 do edital

| Critério | Implementação | Testes existentes | Demonstração |
|---|---|---|---|
| REST, validação e Swagger · 20 pts | [ReminderRuleEndpoints.cs](../src/CadeMinhaConsulta.Api/Features/ReminderRules/ReminderRuleEndpoints.cs), [ReminderRule.cs](../src/CadeMinhaConsulta.Api/Features/ReminderRules/ReminderRule.cs), [NotificationEndpoints.cs](../src/CadeMinhaConsulta.Api/Features/Notifications/NotificationEndpoints.cs) e [SwaggerAuthorizationFilter.cs](../src/CadeMinhaConsulta.Api/Infrastructure/SwaggerAuthorizationFilter.cs). | [ReminderRuleTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ReminderRuleTests.cs) cobre CRUD, entrada inválida, ProblemDetails e header idempotente no Swagger. [ApiInfrastructureTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ApiInfrastructureTests.cs) verifica a documentação das rotas e do Bearer. | Mostrar OpenAPI, criar, listar, consultar, editar e excluir uma regra real. Mostrar `400` para entrada inválida. |
| JWT e autorização · 20 pts | [JwtAuthentication.cs](../src/CadeMinhaConsulta.Api/Infrastructure/JwtAuthentication.cs) valida assinatura, emissor, audiência e expiração. As políticas distinguem cidadão e gestor, e as notificações validam titularidade. | [ApiInfrastructureTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ApiInfrastructureTests.cs) cobre ausência e invalidez do token. [ReminderRuleTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ReminderRuleTests.cs) verifica perfis e rejeição do emissor de demonstração em Production. [NotificationEndpointTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/NotificationEndpointTests.cs) cobre titularidade. | GET anônimo `401`, GET com cidadão `200`, POST com cidadão `403`, POST com gestor `201`. |
| Idempotência · 15 pts | [ReminderRule.cs](../src/CadeMinhaConsulta.Api/Features/ReminderRules/ReminderRule.cs), [MongoReminderRuleStore.cs](../src/CadeMinhaConsulta.Api/Infrastructure/MongoReminderRuleStore.cs) e [MongoNotificationStore.cs](../src/CadeMinhaConsulta.Api/Infrastructure/MongoNotificationStore.cs). | [ReminderRuleTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ReminderRuleTests.cs) cobre replay, conflito, concorrência, confirmação perdida simulada e reinício do host com MongoDB real. [NotificationEndpointTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/NotificationEndpointTests.cs) cobre upsert concorrente e leitura persistida. | Repetir o POST com mesma chave e corpo, conferindo `201`, corpo e `Location`. Alterar o corpo com a mesma chave e mostrar `409`. |
| Rate limiting · 15 pts | Política de janela fixa em [Program.cs](../src/CadeMinhaConsulta.Api/Program.cs), com cota por `sub`. | `RateLimitReturns429AndIsIsolatedBySubject` e `RateLimitIsPerSubjectAndAnonymousRequestsStillReturn401` em [ApiInfrastructureTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ApiInfrastructureTests.cs) e [ReminderRuleTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ReminderRuleTests.cs). | Com limite de dois pedidos, mostrar `200`, `200` e `429` com `Retry-After`. |
| xUnit · 20 pts | [Solução](../CadeMinhaConsulta.slnx) e [projeto de testes](../tests/CadeMinhaConsulta.InfrastructureTests/CadeMinhaConsulta.InfrastructureTests.csproj). | [NotificationProjectionTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/NotificationProjectionTests.cs) verifica IDs e projeção. [OracleMappingTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/OracleMappingTests.cs) verifica SELECT por titular e bloqueio de gravação. A suíte HTTP inclui dois testes com MongoDB real. | Rodar `dotnet test` com o banco de teste configurado e mostrar 47 aprovados. Explicar que o Oracle real foi validado separadamente no Kestrel. |
| Health Checks · 10 pts | Rotas públicas em [Program.cs](../src/CadeMinhaConsulta.Api/Program.cs) e checks de dependência em [DatabaseHealthChecks.cs](../src/CadeMinhaConsulta.Api/Infrastructure/DatabaseHealthChecks.cs). | `LivenessIsHealthyAndReadinessFailsWithoutDatabases` e `ReadinessReturns200OnlyWhenAllRequiredChecksAreHealthy` em [ApiInfrastructureTests.cs](../tests/CadeMinhaConsulta.InfrastructureTests/ApiInfrastructureTests.cs). | Mostrar liveness `200` e readiness `200` com os checks Oracle e MongoDB saudáveis. |

## Ordem de demonstração

1. Inicie o MongoDB e compile a solução conforme o [README](../README.md), em PowerShell 7. Use um banco MongoDB de teste, gere os JWTs em memória e inicie a API em segundo plano para manter o terminal disponível.

   ```powershell
   $env:Mongo__Database = 'vagacerta_test_apresentacao'
   $tokens = & .\tools\New-DevelopmentJwt.ps1 -TestPatientOracleId 2 -SecondTestPatientOracleId 3
   $api = Start-Process dotnet -PassThru -WindowStyle Hidden -ArgumentList @(
       'run', '--no-build', '--project', 'src/CadeMinhaConsulta.Api',
       '--launch-profile', 'CadeMinhaConsulta.Api'
   )
   ```

   Copie os tokens para o Swagger sem exibi-los na apresentação. Eles expiram em 30 minutos. Use `$tokens.Cidadao | Set-Clipboard` ou `$tokens.Gestor | Set-Clipboard` no mesmo terminal. Abra `/swagger`, `/health/live` e `/health/ready`.
2. Na listagem de regras, confirme `401` sem token. Autorize `CIDADAO`, liste as regras e tente um POST válido para obter `403`. Troque para `GESTOR` e envie um POST com `daysBefore: 366` e uma chave válida para mostrar `400`.
3. Crie uma regra com `daysBefore: 2` e uma chave inédita. Repita a criação com a mesma chave e corpo. Confira o mesmo `201`, ID, corpo e `Location`. Mude o valor para 3 no POST com a mesma chave para demonstrar `409`. Faça GET pelo ID e PUT pela rota de edição. Mantenha a regra disponível para o Mobile.
4. No aplicativo React Native, execute `GET /api/reminder-rules` como `CIDADAO`, com a URL do ambiente e o JWT fornecidos em tempo de execução. Mostre a regra recebida e o HTTP `200`. Esta etapa ainda depende da execução pela equipe no app. Consulte o [exemplo React Native](Contrato-Mobile.md). Depois da demonstração da lista, remova a regra como gestor e mostre `204` e GET `404`.
5. No Swagger, mostre `GET /api/notifications/me` com o primeiro titular fictício, repita o GET e confirme os mesmos IDs. Marque uma notificação como lida, confirme `204` e mostre `isRead: true` no GET seguinte. Com o token do outro titular, tente o mesmo PATCH e mostre `404`.
6. Encerre a primeira instância com `$api.Kill($true)` e aguarde `$api.WaitForExit()`. Configure `$env:RateLimiting__PermitLimit = '2'` e inicie uma nova instância pelo comando do primeiro passo. Depois de autorizar, faça três GETs autenticados com o mesmo titular dentro de um minuto. Mostre `Retry-After` junto com o terceiro resultado `429`. Por fim, rode a suíte xUnit com as variáveis MongoDB descritas no [README](../README.md).

## Perguntas do professor

**Por que usar dois bancos?** O Oracle é a fonte de consultas e titularidade. O MongoDB armazena estado de leitura, histórico de notificações e regras de lembrete. O Mobile chama as APIs Java e .NET separadamente.

**Como evitam que um cidadão veja dados de outro?** O JWT é validado localmente. A API extrai o ID do titular de uma claim assinada. O SELECT do EF Core filtra por esse ID e o PATCH do MongoDB exige o mesmo titular. Quando alguém tenta marcar uma notificação alheia, recebe 404.

**O EF Core altera o DBv3?** O contexto de leitura mapeia `TB_GS_CONSULTA` sem chave, bloqueia `SaveChanges` e não aplica migrations. A conta acadêmica usada na demonstração tem permissão de escrita no servidor. As operações da API são somente leitura no Oracle. Uma conta dedicada de leitura acrescentaria uma garantia no próprio banco.

**O que acontece se o POST persistir e a resposta se perder?** Regra e resposta original são gravadas juntas em um documento, protegido por `_id` único. A repetição usa a mesma identidade, chave e corpo e recupera a resposta original. Corpo diferente com a mesma chave retorna 409. Uma falha inconclusiva retorna 503 e permite repetir com a mesma chave.

**A notificação é um evento em tempo real?** O GET observa o status da consulta no Oracle e usa um upsert com ID estável por titular, consulta e status. O instante registrado corresponde à primeira observação. Estados intermediários entre duas consultas podem passar sem registro.

**O que significa readiness 503?** Uma dependência está indisponível ou degradada. O JSON informa o estado por banco. A API pode continuar viva, e o CRUD de regras depende apenas do MongoDB.

**Onde estão as credenciais?** As conexões locais ficam em User Secrets. O script gera chaves privadas efêmeras e descarta a chave de assinatura no fim. Em Production, o issuer de demonstração é rejeitado. A entrega contém código, documentação e exemplos de configuração.
