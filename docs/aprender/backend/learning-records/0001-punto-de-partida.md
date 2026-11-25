# Punto de partida: sabe las piezas de una query, no el resto de Users

Tobi conoce C# y Angular. Dijo que una query necesita "una query, un query handler y el endpoint", y dudó si hace falta un record equivalente al request HTTP. No sabe qué es un closure, un owner, `IOwnedResource`, los facts, `MessageNames`, orquestar, una saga, para qué está RabbitMQ ni por qué existe `Hosts/` con un `Tasks` adentro.

**Implications**: empezar por la slice de lectura (`GetUser`) y resolver la duda del request ahí. Los conceptos de mensajería van al final, apoyados en el cierre de cuenta como caso único.
