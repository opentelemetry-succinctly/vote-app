REM To debug the application without Docker Compose, run this script to launch the dependencies before debugging.
docker run -d --hostname my-rabbitmq-server --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3.9-management
docker run -d --name redis -p 6379:6379 redis:6.2 
docker run -d --name jaeger -p 16686:16686 -p 6831:6831/udp jaegertracing/all-in-one:1.39