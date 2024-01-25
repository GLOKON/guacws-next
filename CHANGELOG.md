## [1.2.9](https://github.com/glokon/guacws-next/compare/v1.2.8...v1.2.9) (2024-01-25)


### Bug Fixes

* **app:** improve stability ([f45d60a](https://github.com/glokon/guacws-next/commit/f45d60aac1450f7629929ebb140fa729aa1edd9f))

## [1.2.8](https://github.com/glokon/guacws-next/compare/v1.2.7...v1.2.8) (2024-01-25)


### Bug Fixes

* **app:** tweak buffers to handle more data and reduce strain on assigning strings ([f8c6558](https://github.com/glokon/guacws-next/commit/f8c6558f4146662f6e87ed3d6dba0b7216f079aa))

## [1.2.7](https://github.com/glokon/guacws-next/compare/v1.2.6...v1.2.7) (2024-01-24)


### Bug Fixes

* **server:** remove uneeded try/catch blocks, reduce memory footprint ([3a48b9f](https://github.com/glokon/guacws-next/commit/3a48b9f3ca100d52a97e82aa122d9fd5022b2887))

## [1.2.6](https://github.com/glokon/guacws-next/compare/v1.2.5...v1.2.6) (2024-01-24)


### Bug Fixes

* **server:** make guacws-next as fast as possible, removed almost all allocations ([1fa47bb](https://github.com/glokon/guacws-next/commit/1fa47bb2c52fd280209b39f67590b3ed936d69c7))

## [1.2.5](https://github.com/glokon/guacws-next/compare/v1.2.4...v1.2.5) (2024-01-24)


### Bug Fixes

* **server:** fix server not using Hsts ([b3328fb](https://github.com/glokon/guacws-next/commit/b3328fb9ea3b005b01f29805bbff76a679f29601))

## [1.2.4](https://github.com/glokon/guacws-next/compare/v1.2.3...v1.2.4) (2024-01-24)


### Bug Fixes

* **app:** build for all platforms ([b0e1a19](https://github.com/glokon/guacws-next/commit/b0e1a1976867ed6467b1af6c77a6cce05ed6ee79))
* **ci:** fix build for platforms ([129117d](https://github.com/glokon/guacws-next/commit/129117de6045c6c67da80d27f6c3ad6fc9168409))

## [1.2.3](https://github.com/glokon/guacws-next/compare/v1.2.2...v1.2.3) (2024-01-24)


### Bug Fixes

* **server:** make ports more configurable ([b103f79](https://github.com/glokon/guacws-next/commit/b103f794ade181fd369df59c6e0ca559237b2a7c))

## [1.2.2](https://github.com/glokon/guacws-next/compare/v1.2.1...v1.2.2) (2024-01-24)


### Bug Fixes

* **app:** improve security ([b49e250](https://github.com/glokon/guacws-next/commit/b49e250028a53478a02a4539c9ddbc22a8e3c865))

## [1.2.1](https://github.com/glokon/guacws-next/compare/v1.2.0...v1.2.1) (2024-01-24)


### Bug Fixes

* **app:** improve performance of proxy slightly ([0b63ba8](https://github.com/glokon/guacws-next/commit/0b63ba81245766781c5f14ce07e2e3b3b99fabf2))

# [1.2.0](https://github.com/glokon/guacws-next/compare/v1.1.1...v1.2.0) (2024-01-23)


### Features

* **performance:** use pipelines all the way from websocket <-> guacd for the best speed possible ([fc9e2c9](https://github.com/glokon/guacws-next/commit/fc9e2c99ffb65a6672a957a4804a6111ab7a94e6))

## [1.1.1](https://github.com/glokon/guacws-next/compare/v1.1.0...v1.1.1) (2024-01-22)


### Bug Fixes

* **websocket:** use pipelines for websockets by default ([ebec567](https://github.com/glokon/guacws-next/commit/ebec5679ca12cbbeeaa245e01f616a6ac8ed3861))

# [1.1.0](https://github.com/glokon/guacws-next/compare/v1.0.15...v1.1.0) (2024-01-22)


### Features

* **performance:** improve performance from reading/writing from GuacD ([70be15e](https://github.com/glokon/guacws-next/commit/70be15e2d9ffc4511655f60a33c854abb27a3b79))

## [1.0.15](https://github.com/glokon/guacws-next/compare/v1.0.14...v1.0.15) (2024-01-22)


### Bug Fixes

* **app:** update logging ([2265265](https://github.com/glokon/guacws-next/commit/2265265f467ccd5a67604b96ddbbb9e39752cfed))

## [1.0.14](https://github.com/glokon/guacws-next/compare/v1.0.13...v1.0.14) (2024-01-22)


### Bug Fixes

* **app:** remove default ssl endpoint ([183d8f9](https://github.com/glokon/guacws-next/commit/183d8f9899918c8a2e158bd1ed0bd577a3a3db25))

## [1.0.13](https://github.com/glokon/guacws-next/compare/v1.0.12...v1.0.13) (2024-01-22)


### Bug Fixes

* **app:** tweak settings to bind ([17474b0](https://github.com/glokon/guacws-next/commit/17474b0b29e7648da2ea8ac87979b872c115e070))

## [1.0.12](https://github.com/glokon/guacws-next/compare/v1.0.11...v1.0.12) (2024-01-22)


### Bug Fixes

* **app:** add ICU library ([3fb4b8b](https://github.com/glokon/guacws-next/commit/3fb4b8bcee17b8c3b97cbcf50d0c8dfa9e6de525))

## [1.0.11](https://github.com/glokon/guacws-next/compare/v1.0.10...v1.0.11) (2024-01-22)


### Bug Fixes

* **app:** fix runtime identifier ([3d64c4b](https://github.com/glokon/guacws-next/commit/3d64c4bf06b142a0e7ce67151915df21a2277c4b))
* **app:** update to target docker image ([52edc74](https://github.com/glokon/guacws-next/commit/52edc74b4ab4f69a5f93e3c5b69978f5f4f0dbe6))

## [1.0.10](https://github.com/glokon/guacws-next/compare/v1.0.9...v1.0.10) (2024-01-21)


### Bug Fixes

* **app:** remove command runtime ID ([514c4b2](https://github.com/glokon/guacws-next/commit/514c4b24994bf759ba08c89b1d7bca81eaea2733))
* **dotnet:** fix build ([7be325c](https://github.com/glokon/guacws-next/commit/7be325cae6db5f5dac12292f09a4da3d310932db))

## [1.0.9](https://github.com/glokon/guacws-next/compare/v1.0.8...v1.0.9) (2024-01-21)


### Bug Fixes

* **app:** mark as self-contained ([e33af21](https://github.com/glokon/guacws-next/commit/e33af21260a590164e9ef66a424672f8f2ecb861))

## [1.0.8](https://github.com/glokon/guacws-next/compare/v1.0.7...v1.0.8) (2024-01-21)


### Bug Fixes

* **dotnet:** improve build ([cfe164b](https://github.com/glokon/guacws-next/commit/cfe164b145499bbe521fb77bc8f602b382a77b97))
* **dotnet:** make app self-contained ([2f9ca30](https://github.com/glokon/guacws-next/commit/2f9ca306c8ffc37ac4773dd097c6271b175b9af6))

## [1.0.7](https://github.com/glokon/guacws-next/compare/v1.0.6...v1.0.7) (2024-01-21)


### Bug Fixes

* **docker:** fix permissions ([314cae2](https://github.com/glokon/guacws-next/commit/314cae23f4a164262d01c6d28a39bd78c93ebcab))

## [1.0.6](https://github.com/glokon/guacws-next/compare/v1.0.5...v1.0.6) (2024-01-21)


### Bug Fixes

* **docker:** fix supervisor permission ([7030223](https://github.com/glokon/guacws-next/commit/7030223d11f9ff65198edd67a2dc64c135723477))

## [1.0.5](https://github.com/glokon/guacws-next/compare/v1.0.4...v1.0.5) (2024-01-21)


### Bug Fixes

* **docker:** fix running server ([bf94c39](https://github.com/glokon/guacws-next/commit/bf94c39ecfd54642e65b4d282c6df5d5fea01406))

## [1.0.4](https://github.com/glokon/guacws-next/compare/v1.0.3...v1.0.4) (2024-01-21)


### Bug Fixes

* **docker:** fix path ([bc76414](https://github.com/glokon/guacws-next/commit/bc764142613c34607dd1e3592489b656c2cb5e0d))

## [1.0.3](https://github.com/glokon/guacws-next/compare/v1.0.2...v1.0.3) (2024-01-21)


### Bug Fixes

* **docker:** add missing file ([d235bcf](https://github.com/glokon/guacws-next/commit/d235bcf05180170af97ba3404d51969c3b66ea14))

## [1.0.2](https://github.com/glokon/guacws-next/compare/v1.0.1...v1.0.2) (2024-01-21)


### Bug Fixes

* **config:** simplify config ([8d8d5ab](https://github.com/glokon/guacws-next/commit/8d8d5ab701d60c4656c3bb49df94fe30f6e29bbe))

## [1.0.1](https://github.com/glokon/guacws-next/compare/v1.0.0...v1.0.1) (2024-01-21)


### Bug Fixes

* **config:** fix default guacd config ([bb9222f](https://github.com/glokon/guacws-next/commit/bb9222fa7be1f1b774543d43d930fc2aa127148b))

# 1.0.0 (2024-01-21)


### Bug Fixes

* **app:** finish tunnel ([a333041](https://github.com/glokon/guacws-next/commit/a333041475c35f91e113cf3ac739982b640dbe0b))
* **app:** ignore non-standard settings from git ([72af553](https://github.com/glokon/guacws-next/commit/72af553d19f745bf801352f55c6c7ba930823ffd))
* **app:** remove development settings ([506b0db](https://github.com/glokon/guacws-next/commit/506b0db03adfaa03a44f5d0fc6b9ce5367e60455))
* **app:** simplify directory structure ([5ef8c71](https://github.com/glokon/guacws-next/commit/5ef8c7150673413eee45418266618d6dbe08d799))
* **app:** update WebSocket middleware to catch all routes ([aff9516](https://github.com/glokon/guacws-next/commit/aff9516474f92f5f7ffd2018382b07ee23e876d3))
* **ci:** fix missing lock file ([8728809](https://github.com/glokon/guacws-next/commit/8728809f10ff58a989fd583a5f5f8c44a82bd838))
* **ci:** specify build ([09051f7](https://github.com/glokon/guacws-next/commit/09051f773405688ca6e428e8107afb2a7c462d12))


### Features

* **guacws-next:** initial release ([3b0c773](https://github.com/glokon/guacws-next/commit/3b0c773708a184e02f6e70bb4bdad570c069b6c7))


### BREAKING CHANGES

* **guacws-next:** Initial release of guacws-next
