# IRailStressTest
Small tool to discover the true througput of IRail.be

You will need dotnet locally.
Run 'ParallellRun' to run 100 processess in parallel. Every process generates around 4queries/sec for around 250 secs.

Note that not every process might start if there is to little memory.

Use ./Analyze.sh to get an overview or ./SecBySec to get a view per second
