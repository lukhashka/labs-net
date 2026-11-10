# Keycloak як код (Лаб. 09, частина 3)

Pulumi-проєкт описує realm `DevRealmDrivers` у Keycloak, який запускає AppHost (`AddKeycloak("keycloak", 8080)`).

| Файл | Що описує |
|---|---|
| `Program.cs` | realm, ролі, resource servers, scopes, ресурси, role policies, scope permissions, clients, users, outputs |
| `Matrix.cs` | матриця з таблиць 6 і 7: scopes кожного resource server і scopes кожної ролі |
| `ClientFactory.cs` | фабрика клієнтів (resource server, Authorization Code, public PKCE, Client Credentials) і extension-метод з audience mappers |

## Що створюється

- Realm `DevRealmDrivers`: реєстрація, скидання пароля, Remember Me, тема `keycloak`, access token 15 хв.
- Realm-ролі `manager`, `user`, `guest` і composite-роль `admin`, що містить `manager` і `user`.
- Resource servers `orders-api`, `products-api`, `payments-api`: Authorization Services, `ENFORCING`, decision strategy `AFFIRMATIVE`.
- На кожному resource server scopes, ресурс, role policy на кожну роль і scope permission на кожну роль за матрицею. `payments-api` видає дозволи лише admin.
- Clients `orders.postman` (Authorization Code + PKCE), `orders.swagger` (public, PKCE) і `orders.aggregator` (Client Credentials, client-роль `service-reader` для service account). Усі мають audience mappers на resource servers.
- Користувачі `alice.admin`, `john.manager`, `carol.user`, `dave.guest` з підтвердженим email і тимчасовим паролем.

## Запуск

Потрібен Pulumi CLI і запущений AppHost: Keycloak на `http://localhost:8080`. Пароль адміністратора Keycloak генерує Aspire, він є в Dashboard: ресурс `keycloak` → Parameters.

```bash
cd infra/keycloak
pulumi login --local
pulumi stack init dev
pulumi config set --secret adminPassword <пароль admin з Aspire Dashboard>
pulumi config set --secret testUserPassword <пароль тестових користувачів>
pulumi preview
pulumi up
pulumi stack output
pulumi stack output ClientSecrets --show-secrets
pulumi destroy        # прибрати після демонстрації
```

`pulumi login --local` тримає стан на цьому ПК без хмари Pulumi. Пароль до стану запитується як passphrase (або змінна `PULUMI_CONFIG_PASSPHRASE`). Файл `Pulumi.dev.yaml` із зашифрованими секретами не потрапляє в репозиторій (`.gitignore`).

## Як отримати токен із permissions

Звичайний access token містить ролі (`realm_access.roles`). Permissions з'являються в RPT (requesting party token), який Keycloak обчислює за policies resource server:

```bash
# 1) токен користувача (direct grant лише для перевірки на стенді)
curl -d grant_type=password -d client_id=orders.postman -d client_secret=<секрет> -d username=john.manager -d password=<пароль> \
  http://localhost:8080/realms/DevRealmDrivers/protocol/openid-connect/token
# 2) RPT для orders-api: claim authorization.permissions зі scopes ролі
curl -H "Authorization: Bearer <access_token>" -d grant_type=urn:ietf:params:oauth:grant-type:uma-ticket -d audience=orders-api \
  http://localhost:8080/realms/DevRealmDrivers/protocol/openid-connect/token
```

Сервіс читає `authorization.permissions[].scopes` у `PermissionAuthorizationHandler` (`Platform.ServiceDefaults/Authentication/Permissions.cs`) і порівнює зі значенням `[RequirePermission("...")]` ендпоінта.
