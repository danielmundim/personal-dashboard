namespace Dashboard.Api.Features.Photos;

/// <summary>Simple generated SVG scenes shown until real photos are uploaded.</summary>
public static class SamplePhotos
{
    public static readonly string[] Names = ["ribeira-dusk.svg", "atlantic-morning.svg", "douro-hills.svg"];

    public static string? Get(string name) => name switch
    {
        "ribeira-dusk.svg" => Scene("#2b1a4a", "#f08a5d", "#f9c784", "#1b2340"),
        "atlantic-morning.svg" => Scene("#9fd3e6", "#e8f4f8", "#ffe7a8", "#2f6f8f"),
        "douro-hills.svg" => Scene("#5b8c5a", "#cfe3a8", "#fff1c1", "#3d5a3a"),
        _ => null,
    };

    private static string Scene(string top, string bottom, string sun, string land) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 1000" preserveAspectRatio="xMidYMid slice">
          <defs>
            <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stop-color="{top}"/><stop offset="1" stop-color="{bottom}"/>
            </linearGradient>
          </defs>
          <rect width="1600" height="1000" fill="url(#sky)"/>
          <circle cx="860" cy="520" r="130" fill="{sun}" opacity="0.9"/>
          <path d="M0 700 C 300 600, 500 760, 800 680 S 1300 600, 1600 690 V1000 H0 Z" fill="{land}" opacity="0.65"/>
          <path d="M0 800 C 400 740, 700 860, 1000 790 S 1400 760, 1600 810 V1000 H0 Z" fill="{land}"/>
        </svg>
        """;
}
