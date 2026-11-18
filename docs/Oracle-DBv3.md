# Oracle DBv3

## Conexão local

A API lê `ConnectionStrings:Oracle` e `Oracle:Schema` do projeto `CadeMinhaConsulta.Api`. O provider existente, `Oracle.EntityFrameworkCore` com ODP.NET Core gerenciado, faz a conexão direta. O Oracle Client nativo não é necessário para essa configuração. [Configuração do ODP.NET Core](https://docs.oracle.com/en/database/oracle/oracle-database/21/odpnt/InstallCoreConfiguration.html)

Use **Gerenciar Segredos do Usuário** no Visual Studio ou execute `tools/Set-OracleLocalSecrets.ps1` interativamente no PowerShell 7. Informe o host, identificador, usuário e schema pelos parâmetros. A senha é solicitada pelo script e gravada em User Secrets por entrada padrão, fora dos argumentos do processo. Use apenas os dados fornecidos pelo proprietário.

O modo `ServiceName` define `CONNECT_DATA=(SERVICE_NAME=...)`. O modo `Sid` define `CONNECT_DATA=(SID=...)`. A configuração local validada usa `ORCL` como SID. A sessão retornou serviço `orcl`, instância `cdb1`, Oracle 19.3 e `RM561450` tanto em `SESSION_USER` quanto em `CURRENT_SCHEMA`. [Conexão ao Oracle Database](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/featConnecting.html)

Na demonstração, a API usa a conta acadêmica do schema, que tem privilégios de escrita. A proteção de leitura está na aplicação. O EF Core mapeia `TB_GS_CONSULTA` sem chave, `SaveChanges` é bloqueado e o fluxo de notificações executa SELECT filtrado pelo titular. O projeto não aplica migrations. Use uma conta dedicada de leitura quando disponível.

## Inspeção do pacote e ordem de implantação

O ZIP corrigido contém 13 declarações `CREATE TABLE`, 9 chaves primárias, 17 chaves estrangeiras, 9 sequences e 9 triggers de IDs no DDL, além de 4 triggers de auditoria e data, 2 packages com especificação e corpo, e povoamento em 3 blocos PL/SQL.

1. Confirme `SESSION_USER` e `CURRENT_SCHEMA` na mesma sessão Oracle a ser usada para operações de implantação. Ambos devem ser exatamente o schema autorizado. Registre nomes, tipos e estados dos objetos próprios e preserve uma exportação, quando viável, em armazenamento local fora do Git.
2. Limpe apenas os objetos próprios inventariados. Trate dependências entre tabelas do próprio schema e respeite erros do Oracle que impeçam um DROP. A limpeza inicial do arquivo do ZIP contém `CASCADE CONSTRAINTS` e deve ser excluída da etapa de criação, pois uma referência de outro schema não deve ser removida.
3. Execute o trecho de criação do DDL, seguido por `TRIGGERS AUDITORIA E REGRA DE DATA FUTURA.sql`, `INSERCAO DE DADOS.sql`, `PACKAGE CONSULTA DADOS.sql` e `PACKAGE GESTAO DE CONSULTAS.sql`. O povoamento contém commits e só deve ser executado uma vez sobre o schema recém-criado.
4. Compare as tabelas em `USER_TABLES` com as 13 esperadas. Consulte `USER_CONSTRAINTS`, `USER_OBJECTS` e `USER_ERRORS`, registre contagens e valide os relacionamentos usando SELECT. Para testar o agendamento no banco de teste, use o overload com ID de saída sob transação controlada e rollback. O overload manual efetua commit.
5. Com MongoDB e Oracle saudáveis, confirme `/health/ready` 200. Use um JWT de desenvolvimento vinculado a uma consulta fictícia para testar o GET e a marcação de leitura no MongoDB, incluindo repetição e uma tentativa com outro titular.

## Implantação verificada

- A identidade da sessão e o inventário foram confirmados antes da limpeza. O backup local, fora do Git, contém o DDL e o esquema XML das duas tabelas antigas, que estavam vazias. Foram removidas duas tabelas, o índice dependente e 12 sequences, somente em `RM561450`. A operação preservou a lixeira do Oracle.
- Foram executados 67 comandos do DDL, 4 blocos de triggers, 3 blocos de povoamento e 4 blocos de packages. A compilação terminou com zero erros e zero objetos inválidos.
- As tabelas ativas têm 9 PKs e 17 FKs habilitadas e validadas. A contagem de PKs exclui uma constraint da lixeira. Há 9 sequences, 13 triggers habilitadas e 2 packages válidos, com especificação e corpo.
- O povoamento gerou 81 usuários fictícios, 80 filas e 96 consultas. As verificações encontraram zero logins duplicados, zero horários ativos duplicados, zero consultas com titular incorreto e zero senhas de teste fora do formato BCrypt.
- Os metadados confirmaram `NUMBER` com escala zero em `ID_CONSULTA` e `ID_USUARIO`, `VARCHAR2(10)` em `STATUS_CONSULTA` e `DATE` em `DATA_HORA_CONSULTA`. Os valores lidos pelo EF Core na API coincidiram com SELECTs diretos para dois titulares.
- `FN_MINHAS_CONSULTAS`, `FN_DADOS_PREVISAO_FALTA` e `PRC_CONSULTAS_PROXIMAS` abriram seus cursores com sucesso.

| Tabela ativa | Registros após o povoamento |
|---|---:|
| `TB_GS_AUDITORIA_DML` | 281 |
| `TB_GS_CONSULTA` | 96 |
| `TB_GS_CONTATO` | 87 |
| `TB_GS_ENDERECO` | 87 |
| `TB_GS_ESPECIALIDADE` | 12 |
| `TB_GS_FILA` | 80 |
| `TB_GS_MEDICO` | 30 |
| `TB_GS_MEDICO_ESPECIALIDADE` | 60 |
| `TB_GS_MEDICO_UNIDADE` | 30 |
| `TB_GS_UN_CONTATO_ENDERECO` | 6 |
| `TB_GS_UNIDADE` | 6 |
| `TB_GS_US_CONTATO_ENDERECO` | 81 |
| `TB_GS_USUARIO` | 81 |
| **Total** | **937** |
