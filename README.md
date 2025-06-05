# Proyecto DaiBot
![Licencia](https://img.shields.io/badge/License-AGPLv3-blue.svg)

## Discord de Soporte:

Para obtener ayuda configurando tu propia instancia de PokeBot, ¡no dudes en unirte al Discord!

[<img src="https://canary.discordapp.com/api/guilds/1369342739581505536/widget.png?style=banner2">](https://discord.gg/WRs22V6DgE)

Cliente de [sys-botbase](https://github.com/olliz0r/sys-botbase) para la automatización del control remoto de consolas Nintendo Switch.

# Capturas de Pantalla
![imagen](https://github.com/user-attachments/assets/9cd4dd57-0095-4353-8fba-5cb5b9417bd6)

# Funciones Destacadas
- **Búsqueda en Vivo del Registro** a través de la pestaña de Logs. Busca cualquier cosa y encuentra resultados rápidamente.

![imagen](https://github.com/user-attachments/assets/820d8892-ae52-4aa6-981a-cb57d1c32690)

- **Soporte para la Bandeja del Sistema** - Al presionar X para cerrar el programa, este se minimiza a la bandeja del sistema. Haz clic derecho en el ícono de PokeBot en la bandeja para salir o controlar el bot.

![imagen](https://github.com/user-attachments/assets/3a30b334-955c-4fb3-b7d8-60cd005a2e18)


# 📱 Accede a PokeBot Desde Cualquier Dispositivo en Tu Red

## Configuración Rápida

### 1. Habilita el Acceso en Red (elige una opción):
- **Opción A:** Clic derecho en PokeBot.exe → Ejecutar como Administrador  
- **Opción B:** Ejecuta en cmd como admin:  
  `netsh http add urlacl url=http://+:8080/ user=Everyone`

### 2. Permitir a Través del Firewall:
Ejecuta en cmd como admin:
```cmd
netsh advfirewall firewall add rule name="DaiBot Web" dir=in action=allow protocol=TCP localport=8080
```

### 3. Conéctate Desde Tu Teléfono:
- Obtén la IP de tu PC: `ipconfig` (busca Dirección IPv4)
- En tu teléfono: `http://TU-IP-DE-PC:8080`
- Ejemplo: `http://192.168.1.100:8080`

## Requisitos
- Estar en la misma red WiFi
- Regla de firewall en Windows (paso 2)
- Permisos de administrador (solo la primera vez)

---


# Comandos del Bot de Intercambio Pokémon

## Comandos Principales de Intercambio

| Comando         | Alias     | Descripción                                          | Uso                                       | Permisos     |
|----------------|-----------|------------------------------------------------------|-------------------------------------------|--------------|
| `trade`        | `t`       | Intercambia un Pokémon desde un set de Showdown o archivo | `.trade [código] <set_showdown>` o adjuntar archivo | Rol de Intercambio |
| `hidetrade`    | `ht`      | Intercambia sin mostrar detalles del embed           | `.hidetrade [código] <set_showdown>` o adjuntar archivo | Rol de Intercambio |
| `batchTrade`   | `bt`      | Intercambia múltiples Pokémon (máx. 3)               | `.bt <sets_separados_por_--->`            | Rol de Intercambio |
| `batchtradezip`| `btz`     | Intercambia múltiples Pokémon desde un archivo zip (máx. 6) | `.btz` + adjuntar archivo zip              | Rol de Intercambio |
| `egg`          | -         | Intercambia un huevo del Pokémon proporcionado       | `.egg [código] <nombre_pokémon>`           | Rol de Intercambio |

## Comandos Especializados de Intercambio

| Comando         | Alias           | Descripción                                               | Uso                                           | Permisos         |
|----------------|------------------|-----------------------------------------------------------|-----------------------------------------------|------------------|
| `dittoTrade`   | `dt`, `ditto`    | Intercambia un Ditto con estadísticas/naturaleza específicas | `.dt [código] <stats> <idioma> <naturaleza>` | Público          |
| `itemTrade`    | `it`, `item`     | Intercambia un Pokémon con el objeto solicitado           | `.it [código] <nombre_objeto>`                | Público          |
| `mysteryegg`   | `me`             | Intercambia un huevo aleatorio con IVs perfectos          | `.me [código]`                                | Público          |
| `mysterymon`   | `mm`             | Intercambia un Pokémon aleatorio con estadísticas perfectas | `.mm [código]`                                | Rol de Intercambio |

## Comandos de Corrección y Clonación

| Comando     | Alias        | Descripción                                              | Uso                | Permisos      |
|-------------|--------------|----------------------------------------------------------|--------------------|---------------|
| `fixOT`     | `fix`, `f`   | Corrige el OT/apodo si se detecta anuncio                | `.fix [código]`    | Rol de FixOT  |
| `clone`     | `c`          | Clona el Pokémon que muestras                           | `.clone [código]`  | Rol de Clonación |
| `dump`      | `d`          | Descarga el Pokémon que muestras                        | `.dump [código]`   | Rol de Dump   |

## Comandos de Eventos y Pokémon Competitivos

| Comando                 | Alias            | Descripción                                             | Uso                                                       | Permisos         |
|------------------------|------------------|---------------------------------------------------------|-----------------------------------------------------------|------------------|
| `listevents`           | `le`             | Lista los archivos de eventos disponibles               | `.le [filtro] [páginaX]`                                  | Público          |
| `eventrequest`         | `er`             | Solicita un evento específico por índice                | `.er <índice>`                                             | Rol de Intercambio |
| `battlereadylist`      | `brl`            | Lista los archivos competitivos disponibles             | `.brl [filtro] [páginaX]`                                 | Público          |
| `battlereadyrequest`   | `brr`, `br`      | Solicita un archivo competitivo por índice              | `.brr <índice>`                                            | Rol de Intercambio |
| `specialrequestpokemon`| `srp`            | Lista o solicita eventos Wondercard                     | `.srp <gen> [filtro] [páginaX]` o `.srp <gen> <índice>`   | Público / Rol de Intercambio |
| `geteventpokemon`      | `gep`            | Descarga un evento como archivo .pk                     | `.gep <gen> <índice> [idioma]`                            | Público          |

## Comandos de Cola y Estado

| Comando       | Alias         | Descripción                                | Uso     | Permisos |
|---------------|---------------|--------------------------------------------|---------|-----------|
| `tradeList`   | `tl`          | Muestra los usuarios en la cola de intercambio | `.tl`    | Admin     |
| `fixOTList`   | `fl`, `fq`    | Muestra los usuarios en la cola de FixOT   | `.fl`    | Admin     |
| `cloneList`   | `cl`, `cq`    | Muestra los usuarios en la cola de clonación | `.cl`    | Admin     |
| `dumpList`    | `dl`, `dq`    | Muestra los usuarios en la cola de dump    | `.dl`    | Admin     |
| `medals`      | `ml`          | Muestra tu conteo de intercambios y medallas | `.ml`    | Público   |

## Comandos de Administración

| Comando       | Alias               | Descripción                                    | Uso                            | Permisos |
|---------------|---------------------|------------------------------------------------|--------------------------------|-----------|
| `tradeUser`   | `tu`, `tradeOther`  | Intercambia archivo al usuario mencionado      | `.tu [código] @usuario` + adjuntar archivo | Admin     |

## Notas de Uso

- **Parámetro Código**: Código de intercambio opcional (8 dígitos). Si no se proporciona, se genera uno aleatorio.
- **Intercambios por Lote**: Separa múltiples sets con `---` en los intercambios por lote.
- **Soporte de Archivos**: Los comandos aceptan tanto sets de Showdown como archivos .pk adjuntos.
- **Permisos**: Diferentes comandos requieren diferentes roles de Discord para su uso.
- **Idiomas**: Los idiomas soportados para eventos incluyen EN, JA, FR, DE, ES, IT, KO, ZH.

## Juegos Compatibles

- Sword/Shield (SWSH)
- Brilliant Diamond/Shining Pearl (BDSP) 
- Legends Arceus (PLA)
- Scarlet/Violet (SV)
- Let's Go Pikachu/Eevee (LGPE)

# Licencia
Consulta el archivo `License.md` para más detalles sobre la licencia.
