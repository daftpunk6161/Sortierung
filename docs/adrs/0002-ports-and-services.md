# ADR 0002: Service-/Port-Schnittstellen für Kernoperationen

## Status
Accepted

## Kontext
Direkte Kopplung zwischen UI und Kernlogik erschwert Tests und Austauschbarkeit.

## Entscheidung
- Kernoperationen laufen über Application-Services.
- Erweiterungspunkte werden als Ports modelliert (z. B. RegionDedupe, Rollback).

## Konsequenzen
- Verbesserte Testbarkeit
- Geringere UI-Kopplung
- Einfachere Integration externer Adapter
