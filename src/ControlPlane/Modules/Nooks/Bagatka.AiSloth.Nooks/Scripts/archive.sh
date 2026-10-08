#!/bin/sh
# $1: the folder of /work to archive, such as ".".
tar -czf - -C /work "$1"
