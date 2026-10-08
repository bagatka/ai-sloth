#!/bin/sh
# A sandbox's router. It forwards the sandbox's traffic to the internet, and to the Docker host only
# at the TCP ports in HOST_PORTS; never to the host's other ports, the networks around it, the
# addresses in BLOCKED (the host's own), or other sandboxes. DNS goes anywhere: Docker resolves the
# sandbox's names through the host's name servers, which may be on the host's network.
# Its default route is its uplink; its other interface is on the sandbox's network, where it takes
# the gateway address, the first of the network, which Docker reserves but leaves unused.
# HOST_PORTS and BLOCKED: space-separated, possibly empty.
set -eu

private="0.0.0.0/8 10.0.0.0/8 100.64.0.0/10 127.0.0.0/8 168.63.129.16/32 169.254.0.0/16 172.16.0.0/12 192.168.0.0/16 198.18.0.0/15 224.0.0.0/3"

uplink=$(ip -o route show default | awk '{ print $5; exit }')
link=$(ip -o -4 address show | awk -v uplink="$uplink" '$2 != "lo" && $2 != uplink { print $2; exit }')
own=$(ip -o -4 address show dev "$link" | awk '{ print $4; exit }')
network=$(ipcalc -n "$own" | cut -d= -f2)
gateway="${network%.*}.$((${network##*.} + 1))/${own#*/}"

{
    echo '*filter'
    echo ':INPUT DROP [0:0]'
    echo ':FORWARD DROP [0:0]'
    echo ':OUTPUT ACCEPT [0:0]'
    echo '-A INPUT -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT'
    echo '-A FORWARD -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT'
    if [ -n "$HOST_PORTS" ]; then
        host=$(getent hosts host.docker.internal | awk '{ print $1; exit }')
        for port in $HOST_PORTS; do
            echo "-A FORWARD -i $link -d $host -p tcp --dport $port -j ACCEPT"
        done
    fi
    echo "-A FORWARD -i $link -p udp --dport 53 -j ACCEPT"
    echo "-A FORWARD -i $link -p tcp --dport 53 -j ACCEPT"
    for destination in $private $BLOCKED; do
        echo "-A FORWARD -d $destination -j DROP"
    done
    echo "-A FORWARD -i $link -o $uplink -j ACCEPT"
    echo 'COMMIT'
    echo '*mangle'
    echo '-A FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu'
    echo 'COMMIT'
    echo '*nat'
    echo "-A POSTROUTING -o $uplink -j MASQUERADE"
    echo 'COMMIT'
} | iptables-restore

# Last, so the sandbox's traffic reaches the router only once every rule is in place.
ip address add "$gateway" dev "$link"

trap 'exit 0' TERM
sleep infinity &
wait
