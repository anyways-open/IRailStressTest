#! /bin/bash


D=`date +"%Y-%m-%d %H:%M:%S"`
cat *.csv | grep -v "QueryDate" | sort > "all $D.csv"


echo -n "TIMEOUTS: "
cat all*.csv | grep "TIMEOUT" | wc -l


echo -n "ERROR: "
cat all*.csv | grep "ERROR" | wc -l


echo -n "FAILED: "
cat all*.csv | grep "FAILED" | wc -l


echo -n "SUCCESSFULL: "
cat all*.csv | grep -v "TIMEOUT" | grep -v "FAILED" | grep -v "ERROR" | wc -l

echo -n "TOTAL: "
cat all*.csv | wc -l
