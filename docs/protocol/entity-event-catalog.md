# Catálogo compilado de eventos de entidade — Rakion v258

Este arquivo é gerado por `tools/extract_entity_event_catalog.py` a partir dos exports
de `entitiesmp.dll` e do relatório runtime de `DumpClientEntityEventCatalog.py`.
`total size` inclui o cabeçalho base de oito bytes; `payload size = total size - 8`.
A presença no binário comprova o contrato da classe, não que todo evento seja emitido
durante uma partida Rakion desta build.
IDs pequenos podem se repetir entre classes: o dispatcher resolve o evento junto com a
classe da entidade de destino, portanto o ID isolado não é uma chave global.

- SHA-256 de `entitiesmp.dll`: `3235f03638a87779afeec43fd975d0f9bf49825551a32acb48795fa15372d621`
- classes `E*` com `GetSizeOf`: 269
- ID e tamanho resolvidos: 269
- não resolvidos: 0

## Eventos reliable de arma, hold e dano fechados

A decompilação de construtores, cópias, produtores e consumidores do player fecha
os corpos abaixo. `vec3f` são três `float32` little-endian. Nomes como `entityWord`
permanecem neutros quando o binário prova o uso, mas não oferece um nome de domínio
inequívoco para o campo.

| ID | Classe | Layout do payload | Evidência de uso |
|---:|---|---|---|
| `0x01910006` | `ESetWeapon` | `i32 weaponSelector; i32 argument` | o primeiro word seleciona os dois caminhos de arma em `CPlayerAnimator::SetWeapon` |
| `0x01910007` | `EShootWeapon` | `vec3f first; vec3f second; u8 shootType; u8 reserved[3]` | `shootType` usa a enum compilada `EShootWeaponType` com valores `0..2` |
| `0x01910008` | `EShootShuriken` | `vec3f first; vec3f second; u8 projectileCount; u8 variant; u16 reserved` | o consumidor itera exatamente `projectileCount`; o produtor observado grava `9` |
| `0x01910009` | `ERequestHoldAttack` | `u32 entityWord; u8 entityIndex; u8 entitySubIndex; u16 reserved; f32 maximumDistance; u32 argument` | `CheckHoldAttack` resolve a entidade e compara a distância com `maximumDistance` |
| `0x0191000A` | `EHoldAttack` | `u32 entityWord; u8 entityIndex; u8 entitySubIndex; u16 reserved0; u32 argument; u8 actorIndex; u8 actorSubIndex; u16 reserved1` | `ExecuteHoldAttack` resolve a entidade e encaminha o hold ao alvo |
| `0x0191000B` | `EPlayerDamage` | `u32 playerId; u8 damageType; u8 damageMotionType; u16 reserved; f32 firstDamageValue; f32 secondDamageValue; vec3f first; vec3f second` | `ReceiveDamage` copia tipos/vetores de `DamageInfo`, calcula os dois escalares e chama `ApplyReceiveDamage` |
| `0x01910016` | `EPlayerDeath` | `vec3f deathVector` | o produtor copia os três componentes do primeiro vetor de `DamageInfo+0x58` |
| `0x01910017` | `ERespawn` | vazio | o construtor contém somente a base de oito bytes |

## Eventos natalinos fechados

`DecompileClientChristmasEvents.py` cruza construtores, copy constructors e o handler ativo do
player. Os bytes de padding não são validados como zero porque os objetos nativos não os
inicializam em todos os construtores.

| ID | Classe | Layout do payload |
|---:|---|---|
| `0x0191001D` | `EChristmasDestroy` | vazio |
| `0x0191001F` | `EChristmasNoticeMessage` | `i32 messageId` |
| `0x01910020` | `EChristmasSetting` | `u8 kind; u8 padding[3]; vec3f position` |
| `0x01910021` | `EEventItemSetting` | `u8 kind; u8 padding[3]; vec3f position` |
| `0x01910022` | `EGetEventItem` | `i32 collectorId; i32 argument` |
| `0x01910023` | `EDestroyEventItem` | `i32 entityId` |
| `0x52B30000` | `ESpawnChristmasBox` | `vec3f position; u8 kind; u8 padding[3]; i32 argument` |
| `0x52B30001` | `EChristmasBoxItemTouch` | `u8 actorId; u8 padding[3]` |
| `0x52B30002` | `EChristmasBoxReceive` | `u8 actorId; u8 padding[3]` |
| `0x52B50000` | `ESpawnEventItem` | `i32 entityId; i32 argument; u8 kind; u8 ownerId; u8 padding[2]` |

Os eventos conhecidos possuem parsers tipados no relay. Isso comprova o transporte, mas não torna
o evento de stage ativo: os dois arquivos natalinos desta build contêm somente `// 종료`, não entram
no `levellist`, e o modelo `SantaSam.smc` não existe nos XFS distribuídos.

| Event ID | Classe | Total | Payload | GetSizeOf | Construtor | Estado |
|---:|---|---:|---:|---:|---:|---|
| `0x00000000` | `EStop` | 8 | 0 | `0x3509FE40` | `0x3509FE50` | `ok` |
| `0x00000001` | `EStart` | 12 | 4 | `0x3509FE80` | `0x350A0EC0` | `ok` |
| `0x00000002` | `EActivate` | 8 | 0 | `0x3509FEA0` | `0x3509FEB0` | `ok` |
| `0x00000002` | `EFloatingScore` | 20 | 12 | `0x3509D640` | `0x3509D650` | `ok` |
| `0x00000003` | `EDeactivate` | 8 | 0 | `0x3509FEE0` | `0x3509FEF0` | `ok` |
| `0x00000004` | `EEnvironmentStart` | 8 | 0 | `0x3509FF20` | `0x3509FF30` | `ok` |
| `0x00000005` | `EEnvironmentStop` | 8 | 0 | `0x3509FF60` | `0x3509FF70` | `ok` |
| `0x00000006` | `EEnd` | 8 | 0 | `0x3509FFA0` | `0x3509FFB0` | `ok` |
| `0x00000007` | `ETrigger` | 16 | 8 | `0x3509FFE0` | `0x350A1020` | `ok` |
| `0x00000008` | `EOpenDoor` | 8 | 0 | `0x350A0000` | `0x350A0010` | `ok` |
| `0x00000009` | `ETeleportMovingBrush` | 8 | 0 | `0x350A0040` | `0x350A0050` | `ok` |
| `0x0000000A` | `EReminder` | 12 | 4 | `0x350A0080` | `0x350A0090` | `ok` |
| `0x0000000B` | `EStartAttack` | 8 | 0 | `0x350A00C0` | `0x350A00D0` | `ok` |
| `0x0000000C` | `EStopAttack` | 8 | 0 | `0x350A0100` | `0x350A0110` | `ok` |
| `0x0000000D` | `EStopBlindness` | 8 | 0 | `0x350A0140` | `0x350A0150` | `ok` |
| `0x0000000E` | `EStopDeafness` | 8 | 0 | `0x350A0180` | `0x350A0190` | `ok` |
| `0x0000000F` | `EReceiveScore` | 12 | 4 | `0x350A01C0` | `0x350A01D0` | `ok` |
| `0x00000010` | `ESecretFound` | 8 | 0 | `0x350A0200` | `0x350A0210` | `ok` |
| `0x00000011` | `EBonus` | 16 | 8 | `0x350A0240` | `0x350A0250` | `ok` |
| `0x00000012` | `EOpenQDoor` | 8 | 0 | `0x350A0280` | `0x350A0290` | `ok` |
| `0x00000013` | `ECloseQDoor` | 8 | 0 | `0x350A02C0` | `0x350A02D0` | `ok` |
| `0x00000014` | `EActivatebySync` | 8 | 0 | `0x350A0300` | `0x350A0310` | `ok` |
| `0x00000015` | `EKilledEnemy` | 24 | 16 | `0x350A0340` | `0x350A1290` | `ok` |
| `0x00000016` | `ESound` | 16 | 8 | `0x350A0360` | `0x350A1360` | `ok` |
| `0x00000017` | `EScroll` | 16 | 8 | `0x350A0380` | `0x350A1420` | `ok` |
| `0x00000018` | `ETextFX` | 16 | 8 | `0x350A03A0` | `0x350A14E0` | `ok` |
| `0x00000019` | `EHudPicFX` | 16 | 8 | `0x350A03C0` | `0x350A15A0` | `ok` |
| `0x0000001A` | `ECredits` | 16 | 8 | `0x350A03E0` | `0x350A1660` | `ok` |
| `0x0000001B` | `ECenterMessage` | 20 | 12 | `0x350A0400` | `0x350A1720` | `ok` |
| `0x0000001C` | `EComputerMessage` | 16 | 8 | `0x350A0420` | `0x350A0430` | `ok` |
| `0x0000001D` | `EVoiceMessage` | 16 | 8 | `0x350A04B0` | `0x350A04C0` | `ok` |
| `0x0000001E` | `EHitBySpaceShipBeam` | 8 | 0 | `0x350A0540` | `0x350A0550` | `ok` |
| `0x00650000` | `EHit` | 8 | 0 | `0x350D3E10` | `0x350D3E20` | `ok` |
| `0x00650001` | `EBrushDestroyed` | 8 | 0 | `0x350D3E50` | `0x350D3E60` | `ok` |
| `0x00650002` | `EStartSounds` | 8 | 0 | `0x350D3E90` | `0x350D3EA0` | `ok` |
| `0x00650003` | `EStopSounds` | 8 | 0 | `0x350D3ED0` | `0x350D3EE0` | `ok` |
| `0x00650004` | `EBrushDeath` | 12 | 4 | `0x350D3F10` | `0x350D3F20` | `ok` |
| `0x00670000` | `EHarbor` | 8 | 0 | `0x351A7BE0` | `0x351A7BF0` | `ok` |
| `0x006B0000` | `EArchitectureDeath` | 8 | 0 | `0x35050320` | `0x35050330` | `ok` |
| `0x006D0000` | `EArchitectureDeath2` | 8 | 0 | `0x35051E30` | `0x35051E40` | `ok` |
| `0x00CC0000` | `EPlaySoundOnce` | 8 | 0 | `0x351AEBE0` | `0x351AEBF0` | `ok` |
| `0x00D10000` | `EFlipTheSwitch` | 16 | 8 | `0x351BC320` | `0x351BC330` | `ok` |
| `0x00D20000` | `EModel2Activate` | 8 | 0 | `0x350CD6C0` | `0x350CD6D0` | `ok` |
| `0x00D20001` | `EModel2Deactivate` | 8 | 0 | `0x350CD700` | `0x350CD710` | `ok` |
| `0x00D30000` | `ESetViewer` | 8 | 0 | `0x3500FEF0` | `0x3500FF00` | `ok` |
| `0x00D90000` | `ERangeModelDestruction` | 8 | 0 | `0x350C8D60` | `0x350C8D70` | `ok` |
| `0x00D90001` | `EModelDeath` | 8 | 0 | `0x350C8DA0` | `0x350C8DB0` | `ok` |
| `0x00DA0000` | `EChangeAnim` | 56 | 48 | `0x3500C1D0` | `0x3500C1E0` | `ok` |
| `0x00DB0000` | `ETeleportActivate` | 8 | 0 | `0x351BE9F0` | `0x351BEA00` | `ok` |
| `0x00DB0001` | `ETeleportDeactivate` | 8 | 0 | `0x351BEA50` | `0x351BEA60` | `ok` |
| `0x00DC0000` | `ECameraStart` | 12 | 4 | `0x3502E350` | `0x3502E8B0` | `ok` |
| `0x00DC0001` | `ECameraStop` | 12 | 4 | `0x3502E370` | `0x3502E970` | `ok` |
| `0x00DE0000` | `EChangeMusic` | 28 | 20 | `0x350DA050` | `0x350DA690` | `ok` |
| `0x00DE0001` | `ENetChangeMusic` | 20 | 12 | `0x350DA070` | `0x350DA720` | `ok` |
| `0x00DF0000` | `EParticlesActivate` | 8 | 0 | `0x3512C8B0` | `0x3512C8C0` | `ok` |
| `0x00DF0001` | `EParticlesDeactivate` | 8 | 0 | `0x3512C910` | `0x3512C920` | `ok` |
| `0x00E10000` | `ECopierTrigger` | 12 | 4 | `0x35041600` | `0x35041610` | `ok` |
| `0x00E30000` | `EChangeGravity` | 12 | 4 | `0x350A40D0` | `0x350A4180` | `ok` |
| `0x00ED0000` | `EWeatherStart` | 8 | 0 | `0x35087DC0` | `0x35087DD0` | `ok` |
| `0x00ED0001` | `EWeatherStop` | 8 | 0 | `0x35087E20` | `0x35087E30` | `ok` |
| `0x00F20000` | `EModel3Activate` | 8 | 0 | `0x350D01D0` | `0x350D01E0` | `ok` |
| `0x00F20001` | `EModel3Deactivate` | 8 | 0 | `0x350D0210` | `0x350D0220` | `ok` |
| `0x00F40000` | `EActivateSwitchPointer` | 8 | 0 | `0x351BD540` | `0x351BD550` | `ok` |
| `0x00F40001` | `EDeactivateSwitchPointer` | 8 | 0 | `0x351BD5A0` | `0x351BD5B0` | `ok` |
| `0x00F60000` | `EMusicChangerTrigger` | 8 | 0 | `0x350D9560` | `0x350D9570` | `ok` |
| `0x01300000` | `ENetSpawnEntity` | 40 | 32 | `0x35081850` | `0x35083F60` | `ok` |
| `0x01320000` | `EScorpmanWakeUp` | 8 | 0 | `0x351A36F0` | `0x351A3700` | `ok` |
| `0x01360000` | `ERestartAttack` | 8 | 0 | `0x3506D230` | `0x3506D240` | `ok` |
| `0x01360001` | `EReconsiderBehavior` | 8 | 0 | `0x3506D290` | `0x3506D2A0` | `ok` |
| `0x01360002` | `EForceWound` | 8 | 0 | `0x3506D2F0` | `0x3506D300` | `ok` |
| `0x01360003` | `EEnemyBaseDeath` | 12 | 4 | `0x3506D390` | `0x3506D3A0` | `ok` |
| `0x01360004` | `EEnemyBaseDamage` | 48 | 40 | `0x3506D480` | `0x35070B90` | `ok` |
| `0x01360005` | `EBlowUp` | 32 | 24 | `0x3506D4C0` | `0x35070C20` | `ok` |
| `0x01400000` | `EDropKamikaze` | 12 | 4 | `0x351D2AE0` | `0x351D2AF0` | `ok` |
| `0x01420000` | `EChangeState` | 8 | 0 | `0x35067580` | `0x35067590` | `ok` |
| `0x014C0000` | `EBrushDestroyedByDevil` | 20 | 12 | `0x35052ED0` | `0x3505AD00` | `ok` |
| `0x014C0001` | `ERegenerationImpuls` | 8 | 0 | `0x35052F10` | `0x35052F20` | `ok` |
| `0x014C0002` | `EDevilCommand` | 32 | 24 | `0x35052FB0` | `0x3505AD60` | `ok` |
| `0x014C0003` | `EDevilContinue` | 8 | 0 | `0x35052FF0` | `0x35053000` | `ok` |
| `0x014C0004` | `EDevilFireElectricity` | 32 | 24 | `0x35053050` | `0x3505ADE0` | `ok` |
| `0x014C0005` | `EDevilStopElectricity` | 8 | 0 | `0x35053090` | `0x350530A0` | `ok` |
| `0x01510000` | `EElectricityStart` | 12 | 4 | `0x3509AA50` | `0x3509AA60` | `ok` |
| `0x01510001` | `EElectricityStop` | 8 | 0 | `0x3509AB00` | `0x3509AB10` | `ok` |
| `0x01530000` | `EStartCounter` | 8 | 0 | `0x3507C900` | `0x3507C910` | `ok` |
| `0x01530001` | `EStopCounter` | 8 | 0 | `0x3507C960` | `0x3507C970` | `ok` |
| `0x01530002` | `ECounterCount` | 8 | 0 | `0x3507C9C0` | `0x3507C9D0` | `ok` |
| `0x015A0000` | `ELarvaArmDestroyed` | 12 | 4 | `0x3508B770` | `0x3508B780` | `ok` |
| `0x015A0001` | `ELarvaRechargePose` | 12 | 4 | `0x3508B7D0` | `0x3508B7E0` | `ok` |
| `0x015A0002` | `ELarvaContinue` | 8 | 0 | `0x3508B830` | `0x3508B840` | `ok` |
| `0x015A0003` | `ELarvaFireLaser` | 36 | 28 | `0x3508B890` | `0x3508CF90` | `ok` |
| `0x015A0004` | `ELarvaHealth` | 12 | 4 | `0x3508B8D0` | `0x3508B8E0` | `ok` |
| `0x015B0000` | `EElementalGrow` | 8 | 0 | `0x35002860` | `0x35002870` | `ok` |
| `0x015B0001` | `EAirElementalContinue` | 8 | 0 | `0x350028C0` | `0x350028D0` | `ok` |
| `0x015C0000` | `ESpinnerInit` | 44 | 36 | `0x351B25B0` | `0x351B28E0` | `ok` |
| `0x015F0000` | `EBatteryBlood` | 20 | 12 | `0x350939B0` | `0x35094030` | `ok` |
| `0x015F0001` | `EBatteryExplode` | 12 | 4 | `0x350939F0` | `0x35093A00` | `ok` |
| `0x015F0002` | `EBatteryDamage` | 12 | 4 | `0x35093A50` | `0x35093A60` | `ok` |
| `0x01610000` | `ELaunchLarvaOffspring` | 12 | 4 | `0x350B3340` | `0x350B3350` | `ok` |
| `0x01620000` | `ESeriousBomb` | 12 | 4 | `0x351A7130` | `0x351A7140` | `ok` |
| `0x01650000` | `EActivateBeam` | 12 | 4 | `0x35094F10` | `0x35094F20` | `ok` |
| `0x01650001` | `EBatteryContinue` | 8 | 0 | `0x35094F70` | `0x35094F80` | `ok` |
| `0x01910000` | `EStartRound` | 16 | 8 | `0x3512FEC0` | `0x3512FED0` | `ok` |
| `0x01910001` | `ERestartRound` | 12 | 4 | `0x3512FF00` | `0x3512FF10` | `ok` |
| `0x01910002` | `EChangeCharaType` | 12 | 4 | `0x3512FF40` | `0x3512FF50` | `ok` |
| `0x01910003` | `EChangeMode` | 12 | 4 | `0x3512FF80` | `0x3512FF90` | `ok` |
| `0x01910004` | `EChangeStart` | 12 | 4 | `0x3512FFC0` | `0x3512FFD0` | `ok` |
| `0x01910005` | `EChangeWeapon` | 8 | 0 | `0x35130000` | `0x35130010` | `ok` |
| `0x01910006` | `ESetWeapon` | 16 | 8 | `0x35130040` | `0x35130050` | `ok` |
| `0x01910007` | `EShootWeapon` | 36 | 28 | `0x35130080` | `0x351347F0` | `ok` |
| `0x01910008` | `EShootShuriken` | 36 | 28 | `0x351300A0` | `0x35134880` | `ok` |
| `0x01910009` | `ERequestHoldAttack` | 24 | 16 | `0x351300C0` | `0x351300D0` | `ok` |
| `0x0191000A` | `EHoldAttack` | 24 | 16 | `0x35130110` | `0x35130120` | `ok` |
| `0x0191000B` | `EPlayerDamage` | 48 | 40 | `0x35130160` | `0x35134950` | `ok` |
| `0x0191000C` | `EPlayerRemainHP` | 20 | 12 | `0x35130180` | `0x35130190` | `ok` |
| `0x0191000D` | `EMageSpell` | 12 | 4 | `0x351301D0` | `0x351301E0` | `ok` |
| `0x0191000E` | `EMageBless` | 24 | 16 | `0x35130210` | `0x35134A20` | `ok` |
| `0x0191000F` | `EMageBarrier` | 20 | 12 | `0x35130230` | `0x35134AA0` | `ok` |
| `0x01910010` | `EMageBomb` | 36 | 28 | `0x35130250` | `0x35134B20` | `ok` |
| `0x01910011` | `EMageMissile` | 12 | 4 | `0x35130270` | `0x35130280` | `ok` |
| `0x01910012` | `EMageDispell` | 12 | 4 | `0x351302C0` | `0x351302D0` | `ok` |
| `0x01910013` | `EPlayerBouncedWall` | 32 | 24 | `0x35130300` | `0x35134BF0` | `ok` |
| `0x01910014` | `EObserver` | 8 | 0 | `0x35130320` | `0x35130330` | `ok` |
| `0x01910015` | `EChaosGuageFull` | 12 | 4 | `0x35130360` | `0x35130370` | `ok` |
| `0x01910016` | `EPlayerDeath` | 20 | 12 | `0x351303A0` | `0x351303B0` | `ok` |
| `0x01910017` | `ERespawn` | 8 | 0 | `0x351303F0` | `0x35130400` | `ok` |
| `0x01910018` | `EEndRound` | 28 | 20 | `0x35130430` | `0x35130440` | `ok` |
| `0x01910019` | `EKillCount` | 24 | 16 | `0x35130480` | `0x35130490` | `ok` |
| `0x0191001A` | `EDamageTypeNet` | 36 | 28 | `0x351304E0` | `0x35134D30` | `ok` |
| `0x0191001B` | `EDisconnected` | 8 | 0 | `0x35130500` | `0x35130510` | `ok` |
| `0x0191001C` | `EAbuseEarnPoint` | 8 | 0 | `0x35130540` | `0x35130550` | `ok` |
| `0x0191001D` | `EChristmasDestroy` | 8 | 0 | `0x35130580` | `0x35130590` | `ok` |
| `0x0191001E` | `EPassiveCreate` | 16 | 8 | `0x351305C0` | `0x35134E10` | `ok` |
| `0x0191001F` | `EChristmasNoticeMessage` | 12 | 4 | `0x351305E0` | `0x351305F0` | `ok` |
| `0x01910020` | `EChristmasSetting` | 24 | 16 | `0x35130620` | `0x35134E70` | `ok` |
| `0x01910021` | `EEventItemSetting` | 24 | 16 | `0x35130640` | `0x35134EF0` | `ok` |
| `0x01910022` | `EGetEventItem` | 16 | 8 | `0x35130660` | `0x35134F70` | `ok` |
| `0x01910023` | `EDestroyEventItem` | 12 | 4 | `0x35130680` | `0x35130690` | `ok` |
| `0x01910024` | `EDamageDiminution` | 16 | 8 | `0x351306C0` | `0x351306D0` | `ok` |
| `0x01910025` | `EUsePotion` | 16 | 8 | `0x35130700` | `0x35130710` | `ok` |
| `0x01910026` | `EQMessage` | 16 | 8 | `0x35130750` | `0x35130760` | `ok` |
| `0x01910027` | `EPrevClearedRank` | 12 | 4 | `0x351307E0` | `0x351307F0` | `ok` |
| `0x01910028` | `EBossDamage` | 16 | 8 | `0x35130820` | `0x35130830` | `ok` |
| `0x01910029` | `EHeart` | 56 | 48 | `0x35130860` | `0x351350C0` | `ok` |
| `0x0191002A` | `ECollisionState` | 12 | 4 | `0x35130880` | `0x35130890` | `ok` |
| `0x0191002B` | `EMovingBrushDeath` | 12 | 4 | `0x351308C0` | `0x351308D0` | `ok` |
| `0x01920000` | `EWeaponsInit` | 12 | 4 | `0x35177950` | `0x35177960` | `ok` |
| `0x01930000` | `EViewInit` | 20 | 12 | `0x35175B70` | `0x35175B80` | `ok` |
| `0x01960000` | `EAnimatorInit` | 12 | 4 | `0x3516B840` | `0x3516B850` | `ok` |
| `0x01980000` | `EModelForSequenceMarker` | 12 | 4 | `0x350CB160` | `0x350CB170` | `ok` |
| `0x01F50000` | `ELaunchProjectile` | 152 | 144 | `0x3517EC60` | `0x35181880` | `ok` |
| `0x01F50001` | `EExplode` | 8 | 0 | `0x3517EC80` | `0x3517EC90` | `ok` |
| `0x01F50002` | `ESpawnFlame` | 28 | 20 | `0x3517ECC0` | `0x3517FB80` | `ok` |
| `0x01F60000` | `EBulletInit` | 16 | 8 | `0x3502CB30` | `0x3502CB40` | `ok` |
| `0x01F80000` | `EFlame` | 16 | 8 | `0x3509BFB0` | `0x3509BFC0` | `ok` |
| `0x01F80001` | `EStopFlaming` | 12 | 4 | `0x3509C070` | `0x3509C080` | `ok` |
| `0x01FA0000` | `ELaunchCannonBall` | 28 | 20 | `0x35033FE0` | `0x35033FF0` | `ok` |
| `0x01FA0001` | `EForceExplode` | 8 | 0 | `0x35034090` | `0x350340A0` | `ok` |
| `0x01FB0000` | `ESpawnerProjectile` | 16 | 8 | `0x351AF920` | `0x351AF930` | `ok` |
| `0x01FB0001` | `EProjectileSpawnClient` | 40 | 32 | `0x351AFA00` | `0x351AFC80` | `ok` |
| `0x02000000` | `ETwister` | 32 | 24 | `0x351C4240` | `0x351C4250` | `ok` |
| `0x02590000` | `ESpawnEffect` | 64 | 56 | `0x350118E0` | `0x350134F0` | `ok` |
| `0x02590001` | `ESummonEffect` | 8 | 0 | `0x35011900` | `0x35011910` | `ok` |
| `0x02590002` | `ESummonEffectOption` | 60 | 52 | `0x35011940` | `0x35013610` | `ok` |
| `0x025A0000` | `ESpawnDebris` | 100 | 92 | `0x3504A8C0` | `0x3504AB50` | `ok` |
| `0x025B0000` | `ESpawnSpray` | 48 | 40 | `0x35024A80` | `0x35024C00` | `ok` |
| `0x025E0000` | `EStormEnvironmentStart` | 8 | 0 | `0x351B35E0` | `0x351B35F0` | `ok` |
| `0x025E0001` | `EStormStart` | 8 | 0 | `0x351B3640` | `0x351B3650` | `ok` |
| `0x025E0002` | `EStormStop` | 8 | 0 | `0x351B36A0` | `0x351B36B0` | `ok` |
| `0x025F0000` | `ETriggerLightning` | 8 | 0 | `0x350B59C0` | `0x350B59D0` | `ok` |
| `0x02600000` | `ESpawnEffector` | 56 | 48 | `0x350654D0` | `0x35065950` | `ok` |
| `0x02600001` | `ETriggerEffector` | 8 | 0 | `0x35065530` | `0x35065540` | `ok` |
| `0x02610000` | `EForcePathMarker` | 12 | 4 | `0x3518BF80` | `0x3518BF90` | `ok` |
| `0x02610001` | `ENetTriggerPSS` | 12 | 4 | `0x3518C030` | `0x3518C040` | `ok` |
| `0x02610002` | `EPyramidShipActivate` | 8 | 0 | `0x3518C090` | `0x3518C0A0` | `ok` |
| `0x02610003` | `EPyramidShipDeactivate` | 8 | 0 | `0x3518C0F0` | `0x3518C100` | `ok` |
| `0x02630000` | `ENetTrigger` | 8 | 0 | `0x35063FF0` | `0x35064000` | `ok` |
| `0x02640000` | `EActivateBlend` | 8 | 0 | `0x35021A40` | `0x35021A50` | `ok` |
| `0x02640001` | `EDeactivateBlend` | 8 | 0 | `0x35021AA0` | `0x35021AB0` | `ok` |
| `0x02690000` | `ESpawnDebrisSka` | 92 | 84 | `0x3504C210` | `0x3504C4A0` | `ok` |
| `0x02700000` | `EExplEffect` | 36 | 28 | `0x35096070` | `0x35096190` | `ok` |
| `0x02710000` | `ESpawnCPEffect` | 28 | 20 | `0x35043140` | `0x350431C0` | `ok` |
| `0x02720000` | `ESpawnFreeze` | 12 | 4 | `0x3509E090` | `0x3509E0A0` | `ok` |
| `0x02BC0000` | `EWatcherInit` | 12 | 4 | `0x351C8970` | `0x351C8980` | `ok` |
| `0x02BC0001` | `EWatch` | 12 | 4 | `0x351C8A60` | `0x351C8A70` | `ok` |
| `0x02BF0000` | `EReminderInit` | 20 | 12 | `0x3519FD50` | `0x3519FD60` | `ok` |
| `0x03200000` | `EReceiveItem` | 8 | 0 | `0x350B0BE0` | `0x350B0BF0` | `ok` |
| `0x03200001` | `EMarkPicked` | 12 | 4 | `0x350B0C80` | `0x350B0C90` | `ok` |
| `0x03200002` | `ESetType` | 12 | 4 | `0x350B0D30` | `0x350B0D40` | `ok` |
| `0x03210000` | `EHealth` | 16 | 8 | `0x350AC7E0` | `0x350AC7F0` | `ok` |
| `0x03220000` | `EWeaponItem` | 20 | 12 | `0x351D0270` | `0x351D0280` | `ok` |
| `0x03230000` | `EAmmoItem` | 16 | 8 | `0x35009E00` | `0x35009E10` | `ok` |
| `0x03240000` | `EArmor` | 16 | 8 | `0x3500EC40` | `0x3500EC50` | `ok` |
| `0x03250000` | `EKey` | 12 | 4 | `0x350B2150` | `0x350B2160` | `ok` |
| `0x03260000` | `EAmmoPackItem` | 40 | 32 | `0x3500B5A0` | `0x3500B5B0` | `ok` |
| `0x03270000` | `EMessageItem` | 16 | 8 | `0x350C6200` | `0x350C6210` | `ok` |
| `0x03280000` | `EPowerUp` | 12 | 4 | `0x3517DB40` | `0x3517DB50` | `ok` |
| `0x032A0000` | `ESaveGame` | 12 | 4 | `0x351C2710` | `0x351C2720` | `ok` |
| `0x032A0001` | `ETreasureItem` | 16 | 8 | `0x351C2770` | `0x351C2780` | `ok` |
| `0x03EB0000` | `ESpawnMessage` | 76 | 68 | `0x35189F60` | `0x35189FE0` | `ok` |
| `0x041A0001` | `EInitRangeWeapon` | 72 | 64 | `0x3519CFD0` | `0x3519D1D0` | `ok` |
| `0x04200000` | `ESpawnBillBoardImage` | 48 | 40 | `0x35020600` | `0x350208E0` | `ok` |
| `0x04210000` | `ESpawnPassive` | 28 | 20 | `0x3512DA30` | `0x3512DD60` | `ok` |
| `0x044D0000` | `EStand` | 8 | 0 | `0x350DC3D0` | `0x350DC3E0` | `ok` |
| `0x044D0001` | `EApproachEnemy` | 8 | 0 | `0x350DC410` | `0x350DC420` | `ok` |
| `0x044D0002` | `ECloseAttack` | 8 | 0 | `0x350DC450` | `0x350DC460` | `ok` |
| `0x044D0003` | `ERangeAttack` | 8 | 0 | `0x350DC490` | `0x350DC4A0` | `ok` |
| `0x044D0004` | `EIdle` | 8 | 0 | `0x350DC4D0` | `0x350DC4E0` | `ok` |
| `0x044D0005` | `ESoulShot` | 36 | 28 | `0x350DC510` | `0x350E0730` | `ok` |
| `0x044D0006` | `ESetTargetforGroup` | 16 | 8 | `0x350DC530` | `0x350E0800` | `ok` |
| `0x044D0007` | `EReportTargetforLeader` | 16 | 8 | `0x350DC550` | `0x350E08E0` | `ok` |
| `0x044D0008` | `ESpawnNpc` | 16 | 8 | `0x350DC570` | `0x350DC580` | `ok` |
| `0x044D0009` | `ENpcReconsiderBehavior` | 8 | 0 | `0x350DC600` | `0x350DC610` | `ok` |
| `0x044D000A` | `ENpcBaseDeath` | 20 | 12 | `0x350DC640` | `0x350DC650` | `ok` |
| `0x044D000B` | `EGoldSword` | 12 | 4 | `0x350DC690` | `0x350DC6A0` | `ok` |
| `0x044D000C` | `ENpcDisappear` | 8 | 0 | `0x350DC6D0` | `0x350DC6E0` | `ok` |
| `0x044D000D` | `ENpcBaseDamage` | 52 | 44 | `0x350DC710` | `0x350E0A60` | `ok` |
| `0x044D000E` | `ENpcHP` | 16 | 8 | `0x350DC730` | `0x350DC740` | `ok` |
| `0x044D000F` | `EAttackHit` | 16 | 8 | `0x350DC770` | `0x350DC780` | `ok` |
| `0x044D0010` | `EAttackFire` | 16 | 8 | `0x350DC7B0` | `0x350DC7C0` | `ok` |
| `0x044D0011` | `ETouchSendedByRemote` | 16 | 8 | `0x350DC7F0` | `0x350DC800` | `ok` |
| `0x044D0012` | `EMovementAnimation` | 12 | 4 | `0x350DC830` | `0x350DC840` | `ok` |
| `0x044D0013` | `EBeIdle` | 8 | 0 | `0x350DC870` | `0x350DC880` | `ok` |
| `0x044D0014` | `ENpcDeadToSwitch` | 12 | 4 | `0x350DC8B0` | `0x350E0C00` | `ok` |
| `0x044D0015` | `EMasterGolemDamage` | 16 | 8 | `0x350DC8D0` | `0x350DC8E0` | `ok` |
| `0x044D0016` | `ENpcExtraSet` | 16 | 8 | `0x350DC910` | `0x350DC920` | `ok` |
| `0x044D0017` | `ENpcSetDummy` | 8 | 0 | `0x350DC950` | `0x350DC960` | `ok` |
| `0x044D0018` | `ENpcSetNormal` | 8 | 0 | `0x350DC990` | `0x350DC9A0` | `ok` |
| `0x044F0000` | `ENpcWatcherInit` | 20 | 12 | `0x3512ACC0` | `0x3512ACD0` | `ok` |
| `0x044F0001` | `ENpcWatch` | 12 | 4 | `0x3512AD50` | `0x3512AD60` | `ok` |
| `0x04570000` | `ESpawnNpcEffect` | 48 | 40 | `0x350F38A0` | `0x350F3930` | `ok` |
| `0x04590000` | `EProjectileSpawnParam` | 148 | 140 | `0x3511E860` | `0x3511F280` | `ok` |
| `0x04590001` | `EExplosion` | 8 | 0 | `0x3511E8A0` | `0x3511E8B0` | `ok` |
| `0x04590002` | `ESpawnBurning` | 28 | 20 | `0x3511E940` | `0x3511EE70` | `ok` |
| `0x04650000` | `EMasterGolemRespawn` | 8 | 0 | `0x35114250` | `0x35114260` | `ok` |
| `0x04690000` | `EGoldGolemRespawn` | 8 | 0 | `0x350FFAA0` | `0x350FFAB0` | `ok` |
| `0x04690001` | `EGoldGolemRebirth` | 8 | 0 | `0x350FFB00` | `0x350FFB10` | `ok` |
| `0x046E0000` | `ESoulCannon` | 40 | 32 | `0x351AB290` | `0x351AB460` | `ok` |
| `0x046E0001` | `ESoulCannonDestroy` | 8 | 0 | `0x351AB2D0` | `0x351AB2E0` | `ok` |
| `0x04700000` | `ELongBow` | 152 | 144 | `0x350B6C20` | `0x350B70A0` | `ok` |
| `0x04930000` | `EFear` | 8 | 0 | `0x35127630` | `0x35127640` | `ok` |
| `0x049A0000` | `EIceWind` | 136 | 128 | `0x350AEA20` | `0x350AF100` | `ok` |
| `0x04B50000` | `EPlayStartMenu` | 8 | 0 | `0x350C3360` | `0x350C3370` | `ok` |
| `0x04B50001` | `EPlayIdleMenu` | 8 | 0 | `0x350C33C0` | `0x350C33D0` | `ok` |
| `0x04B50002` | `EPlayEndMenu` | 8 | 0 | `0x350C3420` | `0x350C3430` | `ok` |
| `0x04B50003` | `EPlayReturnMenu` | 8 | 0 | `0x350C3480` | `0x350C3490` | `ok` |
| `0x24748901` | `ESummonerMaterialize` | 32 | 24 | `0x351B5250` | `0x351B74C0` | `ok` |
| `0x3CAB0000` | `EBarrier` | 24 | 16 | `0x35010690` | `0x35010840` | `ok` |
| `0x3CAC0000` | `EBless` | 28 | 20 | `0x350224C0` | `0x350227F0` | `ok` |
| `0x3CAC0001` | `ECrush` | 8 | 0 | `0x35022500` | `0x35022510` | `ok` |
| `0x3CAD0000` | `EMagicBomb` | 40 | 32 | `0x350B9660` | `0x350B9870` | `ok` |
| `0x3CAE0000` | `EMagicMissile` | 28 | 20 | `0x350BD4C0` | `0x350BD640` | `ok` |
| `0x3CB00000` | `EMageHold` | 20 | 12 | `0x350B8960` | `0x350B8CD0` | `ok` |
| `0x52A90000` | `EQstSWReady` | 12 | 4 | `0x351955E0` | `0x351955F0` | `ok` |
| `0x52A90001` | `EQstSwOn` | 12 | 4 | `0x35195620` | `0x35195630` | `ok` |
| `0x52A90002` | `EQstSwRandom` | 12 | 4 | `0x35195660` | `0x35195670` | `ok` |
| `0x52A90003` | `EQstStatus` | 16 | 8 | `0x351956B0` | `0x351956C0` | `ok` |
| `0x52AB0000` | `ESpawnQstNpc` | 8 | 0 | `0x35194330` | `0x35194340` | `ok` |
| `0x52AD0000` | `ERequestJudge` | 12 | 4 | `0x35192B10` | `0x35192B20` | `ok` |
| `0x52AD0001` | `EPrizeValue` | 24 | 16 | `0x35192B50` | `0x35192B60` | `ok` |
| `0x52AF0000` | `EIndicatorCreate` | 20 | 12 | `0x350AFD80` | `0x350B0070` | `ok` |
| `0x52B10000` | `EMapItemTouch` | 12 | 4 | `0x350C0AF0` | `0x350C0B00` | `ok` |
| `0x52B10001` | `EMapItemReceive` | 12 | 4 | `0x350C0B30` | `0x350C0B40` | `ok` |
| `0x52B10002` | `EMapItemRespawn` | 8 | 0 | `0x350C0B70` | `0x350C0B80` | `ok` |
| `0x52B30000` | `ESpawnChristmasBox` | 28 | 20 | `0x350405B0` | `0x35040800` | `ok` |
| `0x52B30001` | `EChristmasBoxItemTouch` | 12 | 4 | `0x350405D0` | `0x350405E0` | `ok` |
| `0x52B30002` | `EChristmasBoxReceive` | 12 | 4 | `0x35040610` | `0x35040620` | `ok` |
| `0x52B50000` | `ESpawnEventItem` | 20 | 12 | `0x3508A3E0` | `0x3508A3F0` | `ok` |
| `0xFFF18B01` | `ESummonerContinue` | 8 | 0 | `0x351B51F0` | `0x351B5200` | `ok` |
| `0xFFF18B01` | `ESummonerTeleport` | 12 | 4 | `0x351B5190` | `0x351B51A0` | `ok` |
