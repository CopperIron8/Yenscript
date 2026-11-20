namespace Yenscript.Core.Tests

module TestHelpers =
    let unwrap =
        function
        | Ok value -> value
        | Error diagnostics -> failwithf "%A" diagnostics
