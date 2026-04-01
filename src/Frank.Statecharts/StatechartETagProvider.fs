namespace Frank.Statecharts

open System
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Frank

/// Computes ETags for statechart-managed resources by hashing (state, context) pairs.
type StatechartETagProvider<'State, 'Context when 'State: equality>
    (store: IStatechartsStore<'State, 'Context>, contextSerializer: 'Context -> byte[]) =

    let computeETagFromState (state: 'State) (context: 'Context) : string =
        let stateBytes = Encoding.UTF8.GetBytes(string state)
        let contextBytes = contextSerializer context
        let combined = Array.append stateBytes contextBytes
        use sha256 = SHA256.Create()
        let hash = sha256.ComputeHash(combined)
        let truncated = hash.[0..15] // 16 bytes = 128 bits
        let hex = Convert.ToHexString(truncated).ToLowerInvariant()
        hex

    interface IETagProvider with
        member _.ComputeETag(instanceId: string) : Task<string option> =
            task {
                let! result = store.Load(instanceId)

                match result with
                | Some snapshot ->
                    let etag = computeETagFromState snapshot.State snapshot.Context
                    return Some etag
                | None -> return None
            }

/// Factory that creates StatechartETagProvider instances for endpoints with StateMachineMetadata.
type StatechartETagProviderFactory<'State, 'Context when 'State: equality>
    (store: IStatechartsStore<'State, 'Context>, contextSerializer: 'Context -> byte[]) =

    let provider =
        StatechartETagProvider<'State, 'Context>(store, contextSerializer) :> IETagProvider

    interface IETagProviderFactory with
        member _.CreateProvider(endpoint: Microsoft.AspNetCore.Http.Endpoint) : IETagProvider option =
            let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()

            if isNull (box metadata) then None else Some provider

/// DI registration helpers for statechart ETag providers.
[<AutoOpen>]
module StatechartETagProviderExtensions =

    type IServiceCollection with

        /// Registers an IETagProviderFactory that computes ETags from statechart state.
        member services.AddStatechartETagProvider<'State, 'Context when 'State: equality>
            (contextSerializer: 'Context -> byte[])
            : IServiceCollection =
            services.AddSingleton<IETagProviderFactory>(fun sp ->
                let store = sp.GetRequiredService<IStatechartsStore<'State, 'Context>>()

                StatechartETagProviderFactory<'State, 'Context>(store, contextSerializer)
                :> IETagProviderFactory)
