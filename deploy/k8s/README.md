# Ручне розгортання в Kubernetes (Лаб. 10, частини 2 і 3)

Власні маніфести для `catalog-api` і `gateway`. Повну платформу через Aspir8 описано в кореневому README, розділ «Kubernetes (Лаб. 10)».

| Файл | Що містить |
|---|---|
| `catalog-api-deployment.yaml` | 2 репліки, образ `catalog-api:v1`, `imagePullPolicy: IfNotPresent`, порт 8080, startup/liveness `/health/live`, readiness `/health/ready` |
| `catalog-api-service.yaml` | ClusterIP `catalog-api`: порт 80 → 8080, selector `app: catalog-api` |
| `gateway-deployment.yaml`, `gateway-service.yaml` | те саме для YARP Gateway |
| `configmap.yaml` | `gateway-config` (адреси сервісів у форматі `services__<ім'я>__http__0`) і `catalog-config` |
| `catalog-sql.yaml` | SQL Server для Каталогу (демо, `emptyDir`) |

Секретів у цих файлах немає: їх створюють команди нижче, а Deployment підключає їх через `envFrom` → `secretRef`.

## 1. Кластер і образи

Docker Desktop → Settings → Kubernetes → Enable Kubernetes.

```bash
kubectl config use-context docker-desktop
docker build -t catalog-api:v1 -f Services/Catalog/Catalog.Api/Dockerfile .
docker build -t gateway:v1 -f Platform.ApiGateway/Dockerfile .
```

Docker Desktop бачить локальні образи, тому реєстр не потрібен (`imagePullPolicy: IfNotPresent`).

## 2. Secrets: значення поза репозиторієм

```bash
# пароль SQL Server і ключ підпису JWT (Лаб. 09) - випадкові для цього кластера
export SQL_PASSWORD="Sq1!$(openssl rand -hex 12)"
export JWT_KEY="$(openssl rand -hex 32)"
export CATALOG_DB="Server=catalog-sql,1433;Database=CatalogDb;User Id=sa;Password=$SQL_PASSWORD;TrustServerCertificate=True"

kubectl create secret generic catalog-sql-secrets --from-literal=MSSQL_SA_PASSWORD="$SQL_PASSWORD"
kubectl create secret generic catalog-secrets --from-literal=ConnectionStrings__CatalogDb="$CATALOG_DB"
kubectl create secret generic gateway-secrets --from-literal=Jwt__SecretKey="$JWT_KEY"
kubectl create secret generic platform-jwt --from-literal=Jwt__SecretKey="$JWT_KEY"
```

Дані Secret лише закодовані в Base64, а не зашифровані: `kubectl get secret gateway-secrets -o yaml` показує значення, яке легко декодувати. У production використовують шифрування etcd і зовнішнє сховище (Vault, Azure Key Vault).

## 3. Розгортання

```bash
kubectl apply -f deploy/k8s/configmap.yaml -f deploy/k8s/catalog-sql.yaml
kubectl apply -f deploy/k8s/catalog-api-deployment.yaml -f deploy/k8s/catalog-api-service.yaml
kubectl apply -f deploy/k8s/gateway-deployment.yaml -f deploy/k8s/gateway-service.yaml
kubectl get pods,services
kubectl describe pod -l app=catalog-api      # події, стан probes
```

## 4. Запит через Gateway

Після Лаб. 09 Gateway пропускає лише запити з токеном. У ручному розгортанні немає Auth Service, тому для перевірки токен підписується тим самим ключем `JWT_KEY`:

```bash
TOKEN=$(python -c "import base64,hmac,hashlib,json,os,time
b=lambda d: base64.urlsafe_b64encode(json.dumps(d).encode()).rstrip(b'=').decode()
h=b({'alg':'HS256','typ':'JWT'}); now=int(time.time())
p=b({'iss':'campustix-auth','aud':'campustix-api','sub':'k8s-check','unique_name':'k8s-check','role':'User','iat':now,'nbf':now,'exp':now+900})
s=base64.urlsafe_b64encode(hmac.new(os.environ['JWT_KEY'].encode(),f'{h}.{p}'.encode(),hashlib.sha256).digest()).rstrip(b'=').decode()
print(f'{h}.{p}.{s}')")

kubectl port-forward service/gateway 8080:80
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/events      # "products" CampusTix: події Каталогу
kubectl logs deploy/gateway | grep catalog
```

Gateway звертається до `http://catalog-api`. Вбудований DNS Kubernetes перетворює це ім'я на адресу Service, тож IP-адреси ніде не прописуються.

## 5. Перевірки

```bash
kubectl delete pod -l app=catalog-api --wait=false   # Deployment одразу створює нові поди
kubectl get pods -w

kubectl scale deploy/catalog-sql --replicas=0        # база недоступна
kubectl get pods -l app=catalog-api                 # поди Running, але READY 0/1: readiness, а не liveness
kubectl scale deploy/catalog-sql --replicas=1

kubectl edit configmap gateway-config                # зміна видна контейнеру лише після перезапуску
kubectl rollout restart deploy/gateway
```

Readiness перевіряє базу (`/health/ready`), а liveness — лише процес (`/health/live`). Тому недоступна база виводить под з балансування, але не спричиняє нескінченних перезапусків.

## Прибрати

```bash
kubectl delete -f deploy/k8s/
kubectl delete secret catalog-sql-secrets catalog-secrets gateway-secrets platform-jwt
```
