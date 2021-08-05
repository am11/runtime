#!/usr/bin/env bash

if [ "$(uname)" != "Linux" ]; then
    echo "LTTng is not supported on '$(uname)'; only Linux is supported. Skipping.."
    exit 100 # represents success in CLR tests
elif ! command -v lttng 2>/dev/null; then
    echo "error: lttng(1) not found in PATH. Please make sure lttng-tools package is installed on the system."
    exit 1
fi

assemblyName="$2"
sessionName="dotnet-lttng-session-$(tr -dc A-Za-z0-9 </dev/urandom | head -c 8)"

lttng create "$sessionName"
exitCode="$?"
if [ "$exitCode" != 0 ]; then
    echo "error: \"lttng create '$sessionName'\" failed with exit code $exitCode."
    exit "$exitCode"
fi

lttng enable-event -u -a
exitCode="$?"
if [ "$exitCode" != 0 ]; then
    echo "error: \"lttng enable-event -u -a\" failed with exit code $exitCode."
    lttng destroy "$sessionName"
    exit "$exitCode"
fi

lttng start "$sessionName"
exitCode="$?"
if [ "$exitCode" != 0 ]; then
    echo "error: \"lttng start '$sessionName'\" failed with exit code $exitCode."
    lttng destroy "$sessionName"
    exit "$exitCode"
fi

scriptToEvaluate="$(DOTNET_EnableEventLog=1 "$CORE_ROOT/corerun" "$assemblyName")"
exitCode="$?"

lttng stop "$sessionName"

if [ "$exitCode" != 0 ]; then
    echo "error: \"DOTNET_EnableEventLog=1 '$CORE_ROOT/corerun' '$assemblyName'\" failed with exit code $exitCode."
    lttng destroy "$sessionName"
    exit "$exitCode"
fi

eval "$scriptToEvaluate"
exitCode="$?"
if [ "$exitCode" != 0 ]; then
    echo -e "error: failed to evaluate script:\n$scriptToEvaluate"
    lttng destroy "$sessionName"
    exit "$exitCode"
fi

eventIndices="${!events[*]}"
if [ -z "$eventIndices" ]; then
    echo "error: No events recorded by '$assemblyName'."
    lttng destroy "$sessionName"
    exit 1
fi

success=1
iterations=0

# shuffle the array indices and loop over them.
for index in $(printf "%s\n" "${!events[@]}" | shuf); do
    eval "${events["$index"]}"
    exitCode="$?"
    if [ "$exitCode" != 0 ]; then
        echo -e "error: failed to evaluate script:\n$scriptToEvaluate"
        lttng destroy "$sessionName"
        exit "$exitCode"
    fi

    # only test 1000 (random) events.
    if [[ "$iterations" = 1000 ]]; then break; fi
    iterations=$((iterations+1))

    # only query the event name for:
    #   * events without payload.
    #   * "*Bulk*" events from LTTng which have encoded payload.
    #
    keyIndices="${!Keys[*]}"
    if [[ -z "$keyIndices" || "$Name" =~ .*Bulk.* ]]; then
        if ! lttng view "$sessionName" | grep -q "$Name"; then
            echo "error: unable to find event. Name: '$Name' (without payload)"
            success=0
        fi

        continue
    fi

    # for the rest, query the event name and one payload key-value pair at a time.
    for paramIndex in "${!Keys[@]}"; do
        if ! lttng view "$sessionName" | grep -q "$Name.*${Keys["$paramIndex"]}.*${Valuess["$paramIndex"]}"; then
            echo "error: unable to find event. Name: '$Name', Payload: '${Keys["$paramIndex"]} = ${Valuess["$paramIndex"]}'"
            success=0
        fi
    done
done

lttng destroy "$sessionName"

if [ "$success" = 1 ]; then
    echo "LTTng events test passed."
    exit 100 # represents success in CLR tests
else
    echo "LTTng events test failed."
    exit 1
fi
