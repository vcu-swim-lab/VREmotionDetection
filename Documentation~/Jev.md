# Jev (Typesafe)

Jev is a model hosted by Typesafe. The package sends it the player's face-tracking summary and reads back one yes/no probability per emotion. See [docs.typesafe.ai](https://docs.typesafe.ai).

## The API key

The package never ships, stores or reads a key on its own. You provide it as a parameter, like the OpenAI and Anthropic SDKs:

```csharp
var options = new JevOptions(apiKey);                 // required
predictor.UseJev(options);
```

`JevOptions.FromEnvironment()` is a separate, explicit helper that reads `TYPESAFE_API_KEY`. It exists for the Editor and desktop builds; a headset build has no environment variables, so pass the key from your own secure source.

The key is trimmed and checked (no spaces or non-ASCII characters). It is never written into an error message, a log, an id or `ToString()` by the package, and if a server echoes it back it is replaced by `<redacted>` in any text the package shows. It is not serialized into any asset, which is why there is no settings asset: a key in an asset ends up in version control.

**A key inside a shipped build can be extracted.** For a public release, point `BaseUrl` at your own server, which adds the key and forwards the request to Typesafe.

## Options

| Option | Default | Rule |
|---|---|---|
| `ApiKey` (constructor) | none | required |
| `BaseUrl` | `https://api.typesafe.ai` | absolute `https` URL without credentials, query or fragment; `http` only for localhost |
| `Model` | `jev-1.13.0` | not empty; pin a versioned model for stable results |
| `TimeoutSeconds` | 10 | above 0, at most 120 |
| `MaxRetries` | 2 | 0 to 5 |
| `SendOwnFacesInRequest` | false | also send the player's calibration summary (see below) |
| `LogRequests` | false | log one Console line per request: method, URL, status, time and attempt, never the key or the face data |
| `IncludeServerDetails` | false | add the service's own error text to error messages and the log line, after removing the key, `Bearer` and `Authorization` values and control characters, cut to 200 characters |

`Validate()` throws one `ArgumentException` listing every problem, and creating a `JevBackend` (or calling `UseJev`) runs it, so a bad key or URL fails immediately instead of on the first prediction. A backend copies the options when it is created, so changing the object later has no effect.

## What is sent

One request (`POST <BaseUrl>/v1/systemone`) with seven yes/no ("noul") questions, one per emotion. The `state` holds:

- `current_face`: an activity level and the strongest movements as names with a strength word (`strong`, `moderate`, `slight`). No raw numbers.
- `players_own_faces` (only with `SendOwnFacesInRequest` and a ready calibration): for each emotion, the cosine similarity between the current face and the player's face for that emotion, its rank, the player's most characteristic movements, and how many are active now. These are calculated in code, because the model reads words well and does arithmetic badly.

Each question carries a short instruction and the true/false criteria for its emotion. The wording lives in one place, `JevPrompt`, and has a version number that is part of the backend id.

Face data about the player leaves the device. Get the player's consent. Nothing else is sent: no camera image, player name, id or device identifier, and the API key travels only in the `Authorization` header.

## Service, terms and cost

Jev is a third-party paid service run by Typesafe AI. It needs your own account, API key and internet access, and it is billed per input token by Typesafe. Typesafe's [Terms of Use](https://typesafe.ai/legal/terms) and [Privacy Policy](https://typesafe.ai/legal/privacy-policy) apply; retention and deletion are controlled by Typesafe. The README section "Privacy, data and limitations" has the details and the consent advice.

## Results

Jev answers each emotion on its own, so the seven answers do not add up to 1. Every answer must be a number from 0 to 1; anything else makes the whole reply an `InvalidResponse`. The winner is chosen from the answers divided by their sum, but `Predict` returns Jev's **own** answer for the winner as the confidence: a lone weak answer of 0.1 would otherwise read as 100%. `FacePrediction.Raw` holds the unnormalised answers.

## Failures

| Situation | `PredictionErrorCode` | Retried |
|---|---|---|
| 401, 403 | `Unauthorized` | no |
| 400, 404, 422, other 4xx | `InvalidRequest` | no |
| 429 | `RateLimited` | yes, honouring `Retry-After` (at most 10 s) |
| 408, request timeout | `Timeout` | yes |
| no response | `Unreachable` | yes |
| 5xx, 529 | `ServerError` | yes |
| answer cannot be read, is outside 0 to 1, or is all zero | `InvalidResponse` | no |
| the backend was switched away or the predictor disabled during the call | `Canceled` | no |

Retries wait 0.5 s, then 1 s, 2 s and so on, at most 5 s. With the defaults a call that fails every time can take about 31 s (three tries of up to 10 s plus 1.5 s of waiting), and up to about 50 s when the server asks for 10 s waits; lower `TimeoutSeconds` or `MaxRetries` if your game needs a faster verdict.

By default an error message is fixed text for the status ("Typesafe rejected the request"), so the service's reply never reaches your logs or events. With `IncludeServerDetails = true` the reply's text is added after one cleaning step: your key, `Bearer` values and `Authorization`, `x-api-key` and `api-key` values are removed (also when the server wrote them JSON-escaped), control and invisible formatting characters are removed or replaced by spaces, and at most 200 characters are kept. Local network failure text with no response, such as "Cannot resolve destination host", is kept and cleaned the same way; a timeout is always reported as "the request to Typesafe timed out". Do not rely on the cleaning to hide face data: leave `IncludeServerDetails` off in production.

## Cancelling

`JevBackend` implements `ICancelableBackend`. `CancelPending()` aborts the request in flight and the wait before a retry, and the call returns `Canceled` without sending anything more. The predictor calls it when you switch backend, disable the component or destroy it; `Dispose()` does the same. The backend stays usable afterwards. A custom `IHttpTransport` keeps working; to let it abort a request too, implement `ICancelableHttpTransport`, which adds a `CancellationToken` to `PostJsonAsync`.

## Calibration with Jev

Calibration works as it does for every backend: it collects the active backend's scores. With Jev that is about 70 requests per calibration. Jev answers in steps of 0.01 and often says 0.00, which would dominate the calibration's logarithm, so a floor of 0.01 is added before the scores are used (`PrepareForCalibration`). See [Calibration](Calibration.md).

## Checking that requests are sent

Nothing is sent unless a valid face window exists, so a run without face tracking makes no requests and reports `NoTracking`. Set `LogRequests = true` to see each request (`[Jev] POST <url> -> 200 in 412 ms (attempt 1 of 3)`). The server's own text is left out of the line and of errors unless `IncludeServerDetails` is on, and is always cleaned of the key. A request is about 5,000 characters, roughly 1,200 input tokens (about 1,600 with `SendOwnFacesInRequest`).

## Cost

Billed per input token (about $0.042 per million at the time of writing); output is free. One prediction is one request.

## Testing without the network

```csharp
var backend = new JevBackend(options, auNames, new MyFakeTransport());
```

`IHttpTransport.PostJsonAsync` is the only place the network is touched.
