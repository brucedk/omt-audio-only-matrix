This is a proof-of-concept for the Open Media Transport Audio Service. It uses the .NET bindings for OMT to receive audio from a sender and play it through a WASAPI output device. This app was built with Antigravity.

As a proof of concept, this app probably will not be maintained and its not intended for production use. It was created to demonstrate the audio only capabilities of OMT, that can be used for example to send audio to a mixer for live shows, or just send music and instant replay effects from one PC to another over a network.

## Build & Run Instructions

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A working OMT Receiver capable of sending audio (e.g., from the C++ SDK or other bindings)

### Run
1. Run `dotnet run` in the `backend\OMTMediaAudioService` directory
2. Run `npm start dev` in the `frontend` directory
3. For Receive Audio: 
    1. Select the source audio device from the dropdown menu
    2. Select a receiver device to attach the source to
4. For Send Audio: 
    1. Select the local audio device to broadcast from
    2. Enter the stream name to broadcast as
    3. Click Save