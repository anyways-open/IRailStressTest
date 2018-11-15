#! /bin/bash

D=`date +"%Y-%m-%d %H:%M:%S"`
cat *.csv | grep -v "QueryDate" | sort > "all $D.csv"


