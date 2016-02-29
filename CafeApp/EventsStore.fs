module EventsStore
open NEventStore
open NEventStore.Persistence.InMemory
open Chessie.ErrorHandling
open Domain
open Events
open Aggregates
open Errors
open System

type EventStore = {
  GetState : Guid -> Result<State, Error>
  SaveEvent : State * Event -> Result<State * Event, Error>
}
let saveEvent (eventStore : IStoreEvents) (state,event) =
  let tabId = function
    | ClosedTab -> None
    | OpenedTab tab -> Some tab.Id
    | PlacedOrder po -> Some po.TabId
    | OrderInProgress ipo -> Some ipo.PlacedOrder.TabId
    | OrderServed payment -> Some payment.TabId
  match tabId state with
  | Some id ->
      try
        use stream = eventStore.OpenStream(id.ToString())
        stream.Add(EventMessage(Body = event))
        stream.CommitChanges(System.Guid.NewGuid())
        (state,event) |> ok
      with
        | ex -> ErrorWhileSavingEvent ex |> fail
  | None ->
      InvalidStateForSavingEvent |> fail

let getState (eventStore : IStoreEvents) (tabId : System.Guid) =
  try
    use stream = eventStore.OpenStream (tabId.ToString())
    stream.CommittedEvents
    |> Seq.map (fun msg -> msg.Body)
    |> Seq.cast<Event>    
    |> Seq.fold apply ClosedTab
    |> ok
  with
    | ex -> ErrorWhileRetrievingEvents ex |> fail

type InMemoryEventStore () =
  static member Instance =
                  Wireup.Init()
                    .UsingInMemoryPersistence()
                    .Build()

let inMemoryEventStore =
  let instance = InMemoryEventStore.Instance
  {
      SaveEvent = saveEvent instance
      GetState = getState instance
  }